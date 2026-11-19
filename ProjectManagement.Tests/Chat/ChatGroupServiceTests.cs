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
        Assert.Empty(await scope.Chat.SearchAsync("carol", "secret", 50, includeGroups: true));
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
    public async Task AManagerWhoseAccountIsGone_IsReplacedWithoutLeaving()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        await scope.Groups.AddMembersAsync("bob", id, new[] { "carol" });
        // Alice (the manager) is purged by account housekeeping; she never left the group.
        scope.Db.Users.Remove(await scope.Db.Users.SingleAsync(u => u.Id == "alice"));
        await scope.Db.SaveChangesAsync();

        Assert.Equal("bob", (await scope.Groups.ListGroupsAsync("carol")).Single().ManagerUserId);
        Assert.Equal("bob", (await scope.Groups.GetGroupAsync("carol", id))!.ManagerUserId);
        Assert.Equal(ChatGroupStatus.Forbidden, (await scope.Groups.RenameAsync("carol", id, "Mine")).Status);

        var renamed = await scope.Groups.RenameAsync("bob", id, "Bob's desk");
        Assert.Equal(ChatGroupStatus.Ok, renamed.Status);
        Assert.Equal("bob", (await scope.Db.ChatConversations.AsNoTracking().SingleAsync(c => c.Id == id)).ManagerUserId);
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

        var hit = Assert.Single(await scope.Chat.SearchAsync("bob", "minutes", 50, includeGroups: true));
        Assert.Equal(id, hit.GroupId);
        Assert.Equal("TEC team", hit.PeerDisplayName);
        Assert.Empty(await scope.Chat.SearchAsync("bob", "TEC team", 50, includeGroups: true)); // the Created notice is not searched
        // Pages from before group chats do not ask for groups and get none.
        Assert.Empty(await scope.Chat.SearchAsync("bob", "minutes", 50));
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

    [Fact]
    public async Task AnAddedMember_SeesTheGroupOnlyFromWhenTheyJoined()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        await scope.Groups.SendAsync("alice", id, Send("before carol: budget figures"));
        await scope.Groups.SendAsync("bob", id, Send("before carol: noted"));

        var added = await scope.Groups.AddMembersAsync("bob", id, new[] { "carol" });
        await scope.Groups.SendAsync("alice", id, Send("welcome carol"));

        var history = await scope.Groups.GetHistoryAsync("carol", id, null, 40);
        Assert.Equal(new[] { "welcome carol" }, history!.Messages.Where(m => m.EventKind is null).Select(m => m.Body));
        Assert.Contains(history.Messages, m => m.EventKind == ChatGroupEvent.Added); // the notice of joining
        Assert.False(history.HasMore);
        Assert.Equal(1, (await scope.Groups.ListGroupsAsync("carol")).Single().UnreadCount);
        Assert.Empty(await scope.Chat.SearchAsync("carol", "before carol", 50, includeGroups: true));
        Assert.Single(await scope.Chat.SearchAsync("carol", "welcome", 50, includeGroups: true));

        // Paging back from the first visible message finds nothing earlier.
        var first = history.Messages.Min(m => m.Id);
        Assert.Empty((await scope.Groups.GetHistoryAsync("carol", id, first, 40))!.Messages);

        // The people already there keep the whole history.
        Assert.Equal(3, (await scope.Groups.GetHistoryAsync("alice", id, null, 40))!.Messages.Count(m => m.EventKind is null));

        // Members carry when they joined, for "Seen by".
        var carol = added.Group!.Members.Single(m => m.UserId == "carol");
        var alice = added.Group.Members.Single(m => m.UserId == "alice");
        Assert.NotNull(carol.JoinedUtc);
        Assert.True(carol.JoinedUtc > alice.JoinedUtc);
    }

    [Fact]
    public async Task SomeoneAddedBack_DoesNotSeeWhatWasSaidWhileTheyWereOut()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob", "carol" })).Group!.Id;
        await scope.Groups.SendAsync("alice", id, Send("while carol was in"));
        await scope.Groups.RemoveMemberAsync("alice", id, "carol");
        await scope.Groups.SendAsync("alice", id, Send("while carol was out"));
        // Each change is its own web request (its own context): removal deletes in the database
        // directly, so the membership loaded at creation must not linger in this shared context.
        scope.Db.ChangeTracker.Clear();
        await scope.Groups.AddMembersAsync("alice", id, new[] { "carol" });
        await scope.Groups.SendAsync("bob", id, Send("carol is back"));

        var history = await scope.Groups.GetHistoryAsync("carol", id, null, 40);

        Assert.Equal(new[] { "carol is back" }, history!.Messages.Where(m => m.EventKind is null).Select(m => m.Body));
        Assert.Equal(1, (await scope.Groups.ListGroupsAsync("carol")).Single().UnreadCount);
    }

    [Fact]
    public async Task ARetryOrUnsendAfterSomeoneJoined_IsNotDeliveredToTheNewcomer()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        var request = Send("before carol");
        var first = await scope.Groups.SendAsync("alice", id, request);
        await scope.Groups.AddMembersAsync("bob", id, new[] { "carol" });

        // The response to the first send was lost; the browser retries after carol joined.
        var retry = await scope.Groups.SendAsync("alice", id, request);

        Assert.Equal(first.Message!.Id, retry.Message!.Id);
        Assert.Equal(new[] { "alice", "bob" }, retry.MemberUserIds.OrderBy(m => m));

        var unsent = await scope.Chat.UnsendAsync("alice", first.Message.Id);
        Assert.Equal(new[] { "alice", "bob" }, unsent.NotifyUserIds!.OrderBy(m => m));
    }

    private static ChatGroupSendRequest Send(string body) => new(Guid.NewGuid(), body);

    [Fact]
    public async Task TheUnsentCatchUp_LeavesOutMessagesFromBeforeTheCallerJoined()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        var since = DateTimeOffset.MinValue;
        var before = await scope.Groups.SendAsync("alice", id, Send("before carol"));
        await scope.Groups.AddMembersAsync("alice", id, new[] { "carol" });
        var after = await scope.Groups.SendAsync("alice", id, Send("after carol"));
        await scope.Chat.UnsendAsync("alice", before.Message!.Id);
        await scope.Chat.UnsendAsync("alice", after.Message!.Id);

        Assert.Equal(new[] { after.Message.Id }, (await scope.Chat.ListUnsentSinceAsync("carol", since)).Ids);
        Assert.Equal(
            new[] { before.Message.Id, after.Message.Id }.OrderBy(i => i),
            (await scope.Chat.ListUnsentSinceAsync("bob", since)).Ids.OrderBy(i => i));
    }

    [Fact]
    public async Task Mentions_AreStoredForMembersOnly_AndCarriedWithTheMessage()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob", "carol" })).Group!.Id;

        var sent = await scope.Groups.SendAsync("alice", id, new ChatGroupSendRequest(
            Guid.NewGuid(), "@Bob Bhatia please check", MentionedUserIds: new[] { "bob", "alice", "dave", "nobody", "bob" }));

        Assert.Equal(ChatSendStatus.Sent, sent.Status);
        Assert.Equal(new[] { "bob" }, sent.Message!.MentionedUserIds);
        var carolView = (await scope.Groups.GetHistoryAsync("carol", id, null, 40))!.Messages.Last();
        Assert.Equal(new[] { "bob" }, carolView.MentionedUserIds);
    }

    [Fact]
    public async Task Reply_InAGroup_OnlyToAMessageTheSenderCanSee()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        var before = await scope.Groups.SendAsync("alice", id, Send("said before Carol joined"));
        await scope.Groups.AddMembersAsync("alice", id, new[] { "carol" });
        var after = await scope.Groups.SendAsync("bob", id, Send("said after"));

        var hidden = await scope.Groups.SendAsync("carol", id, new ChatGroupSendRequest(Guid.NewGuid(), "quoting what I never saw", ReplyToMessageId: before.Message!.Id));
        Assert.Equal(ChatSendStatus.Invalid, hidden.Status);

        var visible = await scope.Groups.SendAsync("carol", id, new ChatGroupSendRequest(Guid.NewGuid(), "agreed", ReplyToMessageId: after.Message!.Id));
        Assert.Equal(ChatSendStatus.Sent, visible.Status);
        Assert.Equal(after.Message.Id, visible.Message!.ReplyToMessageId);
    }

    [Fact]
    public async Task GroupReactions_AndEdits_ReachOnlyMembersWhoCanSeeTheMessage()
    {
        await using var scope = await GroupScope.CreateAsync();
        var id = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        var early = await scope.Groups.SendAsync("alice", id, Send("Before Carol"));
        await scope.Groups.AddMembersAsync("alice", id, new[] { "carol" });

        // Carol joined later: she cannot react to it, and its changes are not pushed to her.
        Assert.Equal(ChatChangeStatus.NotFound, (await scope.Chat.ReactAsync("carol", early.Message!.Id, ChatReactionKind.Like, true)).Status);
        var reacted = await scope.Chat.ReactAsync("bob", early.Message.Id, ChatReactionKind.Noted, true);
        Assert.Equal(new[] { "alice", "bob" }, reacted.NotifyUserIds.OrderBy(x => x));
        Assert.Equal(id, reacted.Reactions!.GroupId);

        var edit = await scope.Chat.EditAsync("alice", early.Message.Id, new ChatEditRequest("Before Carol (corrected) @Bob", new[] { "bob", "carol" }));
        Assert.Equal(ChatChangeStatus.Done, edit.Status);
        Assert.Equal(id, edit.Message!.GroupId);
        Assert.Equal(new[] { "alice", "bob" }, edit.NotifyUserIds.OrderBy(x => x));
        Assert.Equal(new[] { "bob" }, edit.Message.MentionedUserIds); // not Carol, who cannot see it
        Assert.Equal(new[] { ChatReactionKind.Noted }, edit.Message.Reactions!.Select(r => r.Kind));
    }

    [Fact]
    public async Task CurrentState_OfLoadedMessages_FollowsWhoMayReadThem()
    {
        await using var scope = await GroupScope.CreateAsync();
        var groupId = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        var early = await scope.Groups.SendAsync("alice", groupId, Send("Before Carol"));
        await scope.Groups.AddMembersAsync("alice", groupId, new[] { "carol" });
        var late = await scope.Groups.SendAsync("bob", groupId, Send("After Carol"));
        var direct = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "Just us"));
        await scope.Chat.ReactAsync("bob", early.Message!.Id, ChatReactionKind.Noted, true);
        await scope.Chat.EditAsync("alice", direct.Message!.Id, new ChatEditRequest("Just us two"));
        var ids = new[] { early.Message.Id, late.Message!.Id, direct.Message.Id, 9999L };

        var bob = await scope.Chat.ListCurrentAsync("bob", ids);
        Assert.Equal(new[] { early.Message.Id, late.Message.Id, direct.Message.Id }, bob.Select(m => m.Id).OrderBy(id => id));
        Assert.Equal(ChatReactionKind.Noted, Assert.Single(bob.Single(m => m.Id == early.Message.Id).Reactions!).Kind);
        var edited = bob.Single(m => m.Id == direct.Message.Id);
        Assert.Equal("Just us two", edited.Body);
        Assert.NotNull(edited.EditedUtc);
        Assert.Equal("bob", edited.RecipientUserId);
        Assert.Equal(groupId, bob.Single(m => m.Id == late.Message.Id).GroupId);

        // Carol joined after the first group message and is not in the direct chat.
        var carol = await scope.Chat.ListCurrentAsync("carol", ids);
        Assert.Equal(new[] { late.Message.Id }, carol.Select(m => m.Id));

        // Unsent: reported as unsent, with no text.
        await scope.Chat.UnsendAsync("alice", direct.Message.Id);
        var gone = Assert.Single(await scope.Chat.ListCurrentAsync("bob", new[] { direct.Message.Id }));
        Assert.True(gone.IsUnsent);
        Assert.Equal(string.Empty, gone.Body);
    }

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
