using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Data;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotificationPublisherTests
{
    [Fact]
    public async Task PublishAsync_WithMetadata_PersistsDispatchWithEnvelope()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 9, 0, 0, TimeSpan.Zero));
        var publisher = new NotificationPublisher(context, clock, NullLogger<NotificationPublisher>.Instance);

        var payload = new { Foo = "bar" };

        await publisher.PublishAsync(
            NotificationKind.RemarkCreated,
            new[] { "user-1" },
            payload,
            module: " Remarks ",
            eventType: " Created ",
            scopeType: " Remark ",
            scopeId: " 123 ",
            projectId: 42,
            actorUserId: " actor-1 ",
            route: " /projects/42/remarks/123 ",
            title: " Remark created ",
            summary: " A remark was created. ",
            fingerprint: " remark-123 ");

        var dispatch = Assert.Single(context.NotificationDispatches.AsNoTracking());

        Assert.Equal("user-1", dispatch.RecipientUserId);
        Assert.Equal(NotificationKind.RemarkCreated, dispatch.Kind);
        Assert.Equal("Remarks", dispatch.Module);
        Assert.Equal("Created", dispatch.EventType);
        Assert.Equal("Remark", dispatch.ScopeType);
        Assert.Equal("123", dispatch.ScopeId);
        Assert.Equal(42, dispatch.ProjectId);
        Assert.Equal("actor-1", dispatch.ActorUserId);
        Assert.Equal("/projects/42/remarks/123", dispatch.Route);
        Assert.Equal("Remark created", dispatch.Title);
        Assert.Equal("A remark was created.", dispatch.Summary);
        Assert.Equal("remark-123", dispatch.Fingerprint);
        Assert.Equal(clock.UtcNow.UtcDateTime, dispatch.CreatedUtc);
        Assert.Equal(0, dispatch.AttemptCount);

        using var document = JsonDocument.Parse(dispatch.PayloadJson);
        var root = document.RootElement;

        Assert.Equal("v1", root.GetProperty("version").GetString());
        Assert.Equal("Remarks", root.GetProperty("module").GetString());
        Assert.Equal("Created", root.GetProperty("eventType").GetString());
        Assert.Equal("Remark", root.GetProperty("scopeType").GetString());
        Assert.Equal("123", root.GetProperty("scopeId").GetString());
        Assert.Equal(42, root.GetProperty("projectId").GetInt32());
        Assert.Equal("actor-1", root.GetProperty("actorUserId").GetString());
        Assert.Equal("/projects/42/remarks/123", root.GetProperty("route").GetString());
        Assert.Equal("Remark created", root.GetProperty("title").GetString());
        Assert.Equal("A remark was created.", root.GetProperty("summary").GetString());
        Assert.Equal("remark-123", root.GetProperty("fingerprint").GetString());
        Assert.Equal("bar", root.GetProperty("payload").GetProperty("foo").GetString());
    }

    [Fact]
    public async Task PublishAsync_LegacyOverload_DelegatesToEnrichedVersion()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 10, 0, 0, TimeSpan.Zero));
        var publisher = new NotificationPublisher(context, clock, NullLogger<NotificationPublisher>.Instance);

        var payload = new { Foo = "legacy" };

        await publisher.PublishAsync(
            NotificationKind.RemarkCreated,
            new[] { "legacy-user" },
            payload);

        var dispatch = Assert.Single(context.NotificationDispatches.AsNoTracking());

        Assert.Equal("legacy-user", dispatch.RecipientUserId);
        Assert.Null(dispatch.Module);
        Assert.Null(dispatch.EventType);
        Assert.Null(dispatch.ScopeType);
        Assert.Null(dispatch.ScopeId);
        Assert.Null(dispatch.ProjectId);
        Assert.Null(dispatch.ActorUserId);
        Assert.Null(dispatch.Route);
        Assert.Null(dispatch.Title);
        Assert.Null(dispatch.Summary);
        Assert.Null(dispatch.Fingerprint);

        using var document = JsonDocument.Parse(dispatch.PayloadJson);
        var root = document.RootElement;

        Assert.Equal("v1", root.GetProperty("version").GetString());
        Assert.False(root.TryGetProperty("module", out _));
        Assert.Equal("legacy", root.GetProperty("payload").GetProperty("foo").GetString());
    }

    [Fact]
    public async Task PublishAsync_WithNonPositiveProjectId_Throws()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"notification-tests-{Guid.NewGuid()}")
            .Options;

        await using var context = new ApplicationDbContext(options);
        var clock = new TestClock(new DateTimeOffset(2024, 10, 6, 11, 0, 0, TimeSpan.Zero));
        var publisher = new NotificationPublisher(context, clock, NullLogger<NotificationPublisher>.Instance);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => publisher.PublishAsync(
            NotificationKind.RemarkCreated,
            new[] { "user" },
            new { Foo = "invalid" },
            module: null,
            eventType: null,
            scopeType: null,
            scopeId: null,
            projectId: 0,
            actorUserId: null,
            route: null,
            title: null,
            summary: null,
            fingerprint: null));
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }
}
