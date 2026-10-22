using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Chat;
using ProjectManagement.Services;
using ProjectManagement.Services.Chat;
using Xunit;

namespace ProjectManagement.Tests.Chat;

public sealed class ChatGroupServiceTests
{
    [Fact]
    public async Task Create_MakesTheCreatorManager_AndPostsACreatedNotice()
    {
        await using var scope = await GroupScope.CreateAsync();

        var change = await scope.Groups.CreateAsync("alice", "  TEC   team ", new[] { "bob", "carol", "bob", "alice" });

        Assert.Equal(ChatGroupStatus.Ok, change.Status);
        var group = change.Group!;
        Assert.Equal("TEC team", group.Title);
        Assert.Equal("alice", group.ManagerUserId);
        Assert.Equal(new[] { "alice", "bob", "carol" }, group.Members.Select(m => m.UserId).OrderBy(id => id));
        Assert.Equal(ChatGroupEvent.Created, Assert.Single(change.Events).EventKind);
        Assert.Equal(3, change.NotifyUserIds.Count);

        // A notice is not an unread message.
        Assert.Equal(0, (await scope.Groups.ListGroupsAsync("bob")).Single().UnreadCount);
    }

    [Theory]
    [InlineData("", new[] { "bob" }, "name")]
    [InlineData("Solo", new string[0], "at least one")]
    [InlineData("Inactive", new[] { "bob", "dave" }, "inactive")]
    [InlineData("Unknown", new[] { "bob", "nobody" }, "inactive")]
    public async Task Create_RejectsInvalidRequests(string title, string[] members, string expected)
    {
        await using var scope = await GroupScope.CreateAsync();

        var change = await scope.Groups.CreateAsync("alice", title, members);

        Assert.Equal(ChatGroupStatus.Invalid, change.Status);
        Assert.Contains(expected, change.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await scope.Db.ChatConversations.CountAsync());
    }

    [Fact]
    public async Task Create_RefusesMoreThanFiftyPeople()
    {
        await using var scope = await GroupScope.CreateAsync(extraUsers: 60);
        var many = Enumerable.Range(0, ChatConversation.MaxGroupMembers).Select(i => $"user{i}").ToList();

        var change = await scope.Groups.CreateAsync("alice", "Too big", many);

        Assert.Equal(ChatGroupStatus.Invalid, change.Status);
        Assert.Contains("50", change.Error!);
    }

    [Fact]
    public async Task NonMembers_CannotSeeOrUseAGroup()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Private", new[] { "bob" })).Group!.Id;
        await scope.Groups.SendAsync("alice", id, Send("secret"));

        Assert.Null(await scope.Groups.GetGroupAsync("carol", id));
        Assert.Null(await scope.Groups.GetHistoryAsync("carol", id, null, 40));
        Assert.Equal(ChatSendStatus.RecipientUnavailable, (await scope.Groups.SendAsync("carol", id, Send("let me in"))).Status);
        Assert.Null((await scope.Groups.MarkReadAsync("carol", id, 999)).Marker);
        Assert.Null(await scope.Groups.OtherMembersAsync("carol", id));
        Assert.Equal(ChatGroupStatus.NotFound, (await scope.Groups.AddMembersAsync("carol", id, new[] { "carol" })).Status);
        Assert.Empty(await scope.Groups.ListGroupsAsync("carol"));
        Assert.Empty(await scope.Chat.SearchAsync("carol", "secret", 50));
    }

    [Fact]
    public async Task Messages_CountUnreadForOthers_AndReadMarkersShowInHistory()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob", "carol" })).Group!.Id;

        var first = await scope.Groups.SendAsync("alice", id, Send("one"));
        var second = await scope.Groups.SendAsync("bob", id, Send("two"));

        Assert.Equal(ChatSendStatus.Sent, first.Status);
        Assert.Equal(id, first.Message!.GroupId);
        Assert.Equal(3, first.MemberUserIds.Count);
        Assert.Equal(2, (await scope.Groups.ListGroupsAsync("carol")).Single().UnreadCount);
        Assert.Equal(1, (await scope.Groups.ListGroupsAsync("alice")).Single().UnreadCount);

        var (marker, _) = await scope.Groups.MarkReadAsync("carol", id, second.Message!.Id);
        Assert.Equal(second.Message.Id, marker);
        Assert.Equal(0, (await scope.Groups.ListGroupsAsync("carol")).Single().UnreadCount);

        var history = await scope.Groups.GetHistoryAsync("alice", id, null, 40);
        Assert.Equal(new[] { "one", "two" }, history!.Messages.Where(m => m.EventKind is null).Select(m => m.Body));
        Assert.Equal(second.Message.Id, history.Members.Single(m => m.UserId == "carol").LastReadMessageId);
    }

    [Fact]
    public async Task Send_RetryWithSameClientId_ReturnsTheStoredMessage()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        var request = Send("once");

        var first = await scope.Groups.SendAsync("alice", id, request);
        var retry = await scope.Groups.SendAsync("alice", id, request);

        Assert.Equal(first.Message!.Id, retry.Message!.Id);
        Assert.Equal(1, await scope.Db.ChatMessages.CountAsync(m => m.EventKind == null));
        // The same id cannot be replayed into a direct conversation.
        Assert.Equal(ChatSendStatus.Invalid, (await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", request.ClientMessageId, "once"))).Status);
    }

    [Fact]
    public async Task AnyMemberCanAdd_OnlyTheManagerCanRemoveOrRename()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;

        var added = await scope.Groups.AddMembersAsync("bob", id, new[] { "carol", "bob" });
        Assert.Equal(ChatGroupStatus.Ok, added.Status);
        Assert.Equal(ChatGroupEvent.Added, Assert.Single(added.Events).EventKind);
        Assert.Equal("carol", added.Events[0].EventData);

        Assert.Equal(ChatGroupStatus.Forbidden, (await scope.Groups.RemoveMemberAsync("bob", id, "carol")).Status);
        Assert.Equal(ChatGroupStatus.Forbidden, (await scope.Groups.RenameAsync("bob", id, "Mine now")).Status);

        var renamed = await scope.Groups.RenameAsync("alice", id, "Front desk");
        Assert.Equal("Front desk", renamed.Group!.Title);
        Assert.Equal(ChatGroupEvent.Renamed, renamed.Events.Single().EventKind);

        var removed = await scope.Groups.RemoveMemberAsync("alice", id, "carol");
        Assert.Equal(ChatGroupStatus.Ok, removed.Status);
        Assert.Equal(new[] { "carol" }, removed.RemovedUserIds);
        Assert.Null(await scope.Groups.GetHistoryAsync("carol", id, null, 40));
        Assert.Equal(ChatGroupStatus.Invalid, (await scope.Groups.RemoveMemberAsync("alice", id, "alice")).Status);
    }

    [Fact]
    public async Task ManagerLeaving_PassesManagementToTheLongestStandingMember()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        await scope.Groups.AddMembersAsync("bob", id, new[] { "carol" });

        var left = await scope.Groups.LeaveAsync("alice", id);

        Assert.Equal(ChatGroupStatus.Ok, left.Status);
        Assert.Equal("bob", left.Group!.ManagerUserId);
        Assert.Equal(new[] { "alice" }, left.RemovedUserIds);
        Assert.Equal(ChatGroupEvent.Left, left.Events.Single().EventKind);
        Assert.Null(await scope.Groups.GetGroupAsync("alice", id));
        Assert.Equal(ChatGroupStatus.Ok, (await scope.Groups.RenameAsync("bob", id, "Bob's desk")).Status);
    }

    [Fact]
    public async Task ManagerLeaving_SkipsMembersWhoseAccountsAreGone()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        await scope.Groups.AddMembersAsync("bob", id, new[] { "carol" });
        // Bob (the longest-standing after Alice) is purged; his membership row stays behind.
        scope.Db.Users.Remove(await scope.Db.Users.SingleAsync(u => u.Id == "bob"));
        await scope.Db.SaveChangesAsync();

        var left = await scope.Groups.LeaveAsync("alice", id);

        Assert.Equal("carol", left.Group!.ManagerUserId);
        Assert.Equal(ChatGroupStatus.Ok, (await scope.Groups.RenameAsync("carol", id, "Carol's desk")).Status);
    }

    [Fact]
    public async Task ManagerLeaving_WithNoActiveMemberLeft_LeavesTheGroupUnmanaged()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        scope.Db.Users.Remove(await scope.Db.Users.SingleAsync(u => u.Id == "bob"));
        await scope.Db.SaveChangesAsync();

        var left = await scope.Groups.LeaveAsync("alice", id);

        Assert.Equal(ChatGroupStatus.Ok, left.Status);
        Assert.Equal(string.Empty, left.Group!.ManagerUserId);
    }

    [Fact]
    public async Task AFailedChange_LeavesNothingBehind()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        var before = await scope.Db.ChatMessages.CountAsync();

        Assert.Equal(ChatGroupStatus.Invalid, (await scope.Groups.AddMembersAsync("alice", id, new[] { "dave" })).Status);

        Assert.Equal(before, await scope.Db.ChatMessages.CountAsync());
        Assert.Equal(2, await scope.Db.ChatParticipants.CountAsync(p => p.ConversationId == id));
    }

    [Fact]
    public async Task Groups_StayOutOfTheContactList_ButAreSearchable()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "TEC team", new[] { "bob" })).Group!.Id;
        await scope.Groups.SendAsync("alice", id, Send("TEC minutes attached"));

        var bobAsContact = (await scope.Chat.ListContactsAsync("alice")).Single(c => c.UserId == "bob");
        Assert.Equal(0, bobAsContact.UnreadCount);
        Assert.Null(bobAsContact.LastMessageUtc);

        var hit = Assert.Single(await scope.Chat.SearchAsync("bob", "minutes", 50));
        Assert.Equal(id, hit.GroupId);
        Assert.Equal("TEC team", hit.PeerDisplayName);
        Assert.Empty(await scope.Chat.SearchAsync("bob", "TEC team", 50)); // the Created notice is not searched
    }

    [Fact]
    public async Task Unsend_InAGroup_NotifiesEveryMember_AndNoticesCannotBeUnsent()
    {
        await using var scope = await GroupScope.CreateAsync();
        var created = await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob", "carol" });
        var id = created.Group!.Id;
        var sent = await scope.Groups.SendAsync("alice", id, Send("oops"));

        var result = await scope.Chat.UnsendAsync("alice", sent.Message!.Id);

        Assert.Equal(ChatUnsendStatus.Unsent, result.Status);
        Assert.Equal(id, result.Message!.GroupId);
        Assert.Equal(3, result.NotifyUserIds!.Count);
        Assert.Equal(ChatUnsendStatus.NotFound, (await scope.Chat.UnsendAsync("alice", created.Events[0].Id)).Status);
    }

    private static ChatGroupSendRequest Send(string body) => new(Guid.NewGuid(), body);

    private sealed class GroupScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private GroupScope(SqliteConnection connection, ApplicationDbContext db)
        {
            _connection = connection;
            Db = db;
            var clock = new StepClock();
            Chat = new ChatService(db, clock, new ChatPresenceTracker(), new TestOptions());
            Groups = new ChatGroupService(db, clock);
        }

        public ApplicationDbContext Db { get; }

        public ChatService Chat { get; }

        public ChatGroupService Groups { get; }

        public static async Task<GroupScope> CreateAsync(int extraUsers = 0)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                Human("alice", "Alice Anand"),
                Human("bob", "Bob Bhatia"),
                Human("carol", "Carol Chauhan"),
                new ApplicationUser { Id = "dave", UserName = "dave", FullName = "Dave Dutta", AccountKind = UserAccountKind.Human, IsDisabled = true });
            for (var i = 0; i < extraUsers; i++)
            {
                db.Users.Add(Human($"user{i}", $"User {i}"));
            }

            await db.SaveChangesAsync();
            return new GroupScope(connection, db);
        }

        private static ApplicationUser Human(string id, string name)
            => new() { Id = id, UserName = id, FullName = name, AccountKind = UserAccountKind.Human };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StepClock : IClock
    {
        private DateTimeOffset _now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }

    private sealed class TestOptions : IOptionsMonitor<ChatOptions>
    {
        public ChatOptions CurrentValue { get; } = new();

        public ChatOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ChatOptions, string?> listener) => null;
    }
}
