using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Hubs;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Chat;
using Xunit;

namespace ProjectManagement.Tests.Chat;

public sealed class ChatHubTests
{
    [Fact]
    public async Task Typing_IsForwardedToThatPersonOnly_AtMostOncePerInterval()
    {
        await using var scope = await HubScope.CreateAsync();
        var hub = scope.HubFor("alice");
        await hub.OnConnectedAsync();
        scope.Pushes.Clear();

        await hub.Typing("bob");
        await hub.Typing("bob"); // within two seconds: dropped
        await hub.Typing("alice"); // to oneself: ignored

        var push = Assert.Single(scope.Pushes);
        Assert.Equal(nameof(IChatClient.Typing), push.Method);
        Assert.Equal(new[] { "bob" }, push.Targets);
    }

    [Fact]
    public async Task Typing_ToManyDifferentPeople_IsCappedPerConnection()
    {
        await using var scope = await HubScope.CreateAsync();
        var hub = scope.HubFor("alice");
        await hub.OnConnectedAsync();
        scope.Pushes.Clear();

        for (var i = 0; i < ChatHub.MaxTypingTargets + 10; i++)
        {
            await hub.Typing($"someone{i}");
        }

        Assert.Equal(ChatHub.MaxTypingTargets, scope.Pushes.Count);
        var state = Assert.IsType<Dictionary<string, DateTimeOffset>>(scope.Context.Items["chat.typing"]);
        Assert.Equal(ChatHub.MaxTypingTargets, state.Count);
    }

    [Fact]
    public async Task TypingInGroup_GoesToTheOtherMembers_AndNothingForNonMembers()
    {
        await using var scope = await HubScope.CreateAsync();
        var groupId = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;

        var alice = scope.HubFor("alice");
        await alice.OnConnectedAsync();
        scope.Pushes.Clear();
        await alice.TypingInGroup(groupId);
        var push = Assert.Single(scope.Pushes);
        Assert.Equal(nameof(IChatClient.GroupTyping), push.Method);
        Assert.Equal(new[] { "bob" }, push.Targets);

        var carol = scope.HubFor("carol");
        await carol.OnConnectedAsync();
        scope.Pushes.Clear();
        await carol.TypingInGroup(groupId);
        Assert.Empty(scope.Pushes);
    }

    [Fact]
    public async Task SignalsBeforeTheConnectionIsCountedIn_AreIgnored()
    {
        await using var scope = await HubScope.CreateAsync();
        var hub = scope.HubFor("alice"); // OnConnectedAsync not run (e.g. chat switched off)

        await hub.Typing("bob");
        await hub.SetAway(true);

        Assert.Empty(scope.Pushes);
    }

    private sealed class HubScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly RecordingHubContext _recorder = new();
        private readonly ChatPresenceTracker _presence = new();

        private HubScope(SqliteConnection connection, ApplicationDbContext db)
        {
            _connection = connection;
            Db = db;
            Groups = new ChatGroupService(db, new SystemClockStub());
        }

        public ApplicationDbContext Db { get; }

        public ChatGroupService Groups { get; }

        public List<ChatPush> Pushes => _recorder.Pushes;

        public FakeCallerContext Context { get; private set; } = null!;

        public static async Task<HubScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(
                new ApplicationUser { Id = "alice", UserName = "alice", FullName = "Alice", AccountKind = UserAccountKind.Human },
                new ApplicationUser { Id = "bob", UserName = "bob", FullName = "Bob", AccountKind = UserAccountKind.Human },
                new ApplicationUser { Id = "carol", UserName = "carol", FullName = "Carol", AccountKind = UserAccountKind.Human });
            await db.SaveChangesAsync();
            return new HubScope(connection, db);
        }

        public ChatHub HubFor(string userId)
        {
            Context = new FakeCallerContext(userId);
            return new ChatHub(_presence, new EnabledOptions(), Groups)
            {
                Context = Context,
                Clients = new CallerClients(_recorder),
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class FakeCallerContext : HubCallerContext
    {
        private readonly string _userId;

        public FakeCallerContext(string userId)
        {
            _userId = userId;
        }

        public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");

        public override string? UserIdentifier => _userId;

        public override ClaimsPrincipal? User => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, _userId) }, "Test"));

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }

    private sealed class CallerClients : IHubCallerClients<IChatClient>
    {
        private readonly RecordingHubContext _recorder;

        public CallerClients(RecordingHubContext recorder)
        {
            _recorder = recorder;
        }

        private IHubClients<IChatClient> Hub => _recorder;

        public IChatClient Caller => Hub.Client("caller");

        public IChatClient Others => Hub.All;

        public IChatClient OthersInGroup(string groupName) => Hub.Group(groupName);

        public IChatClient All => Hub.All;

        public IChatClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => Hub.AllExcept(excludedConnectionIds);

        public IChatClient Client(string connectionId) => Hub.Client(connectionId);

        public IChatClient Clients(IReadOnlyList<string> connectionIds) => Hub.Clients(connectionIds);

        public IChatClient Group(string groupName) => Hub.Group(groupName);

        public IChatClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Hub.GroupExcept(groupName, excludedConnectionIds);

        public IChatClient Groups(IReadOnlyList<string> groupNames) => Hub.Groups(groupNames);

        public IChatClient User(string userId) => Hub.User(userId);

        public IChatClient Users(IReadOnlyList<string> userIds) => Hub.Users(userIds);
    }

    private sealed class EnabledOptions : IOptionsMonitor<ChatOptions>
    {
        public ChatOptions CurrentValue { get; } = new();

        public ChatOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ChatOptions, string?> listener) => null;
    }

    private sealed class SystemClockStub : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
