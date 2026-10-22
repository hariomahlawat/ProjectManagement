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

public sealed class ChatServiceTests
{
    [Fact]
    public async Task Send_CreatesOneConversation_AndCountsUnreadForTheRecipientOnly()
    {
        await using var scope = await ChatScope.CreateAsync();

        var first = await scope.Service.SendAsync("alice", Request("bob", "Hello Bob"));
        var reply = await scope.Service.SendAsync("bob", Request("alice", "Hi Alice"));
        var second = await scope.Service.SendAsync("alice", Request("bob", "Lunch?"));

        Assert.Equal(ChatSendStatus.Sent, first.Status);
        Assert.Equal(ChatSendStatus.Sent, reply.Status);
        Assert.Equal("bob", first.Message!.RecipientUserId);
        Assert.Equal(1, await scope.Db.ChatConversations.CountAsync());
        Assert.Equal(2, await scope.Db.ChatParticipants.CountAsync());

        var bobView = await scope.Service.ListContactsAsync("bob");
        var aliceView = await scope.Service.ListContactsAsync("alice");
        Assert.Equal(2, bobView.Single(c => c.UserId == "alice").UnreadCount);
        Assert.Equal(1, aliceView.Single(c => c.UserId == "bob").UnreadCount);
        Assert.NotNull(aliceView.Single(c => c.UserId == "bob").LastMessageUtc);
        Assert.True(second.Message!.Id > first.Message.Id);
    }

    [Fact]
    public async Task Send_RetryWithSameClientId_ReturnsStoredMessageWithoutDuplicating()
    {
        await using var scope = await ChatScope.CreateAsync();
        var clientId = Guid.NewGuid();

        var first = await scope.Service.SendAsync("alice", new ChatSendRequest("bob", clientId, "Once"));
        var retry = await scope.Service.SendAsync("alice", new ChatSendRequest("bob", clientId, "Once"));

        Assert.Equal(ChatSendStatus.Sent, retry.Status);
        Assert.Equal(first.Message!.Id, retry.Message!.Id);
        Assert.Equal(1, await scope.Db.ChatMessages.CountAsync());
    }

    [Fact]
    public async Task Send_ReusingClientIdForAnotherPerson_IsRejected()
    {
        await using var scope = await ChatScope.CreateAsync();
        var clientId = Guid.NewGuid();

        await scope.Service.SendAsync("alice", new ChatSendRequest("bob", clientId, "To Bob"));
        var misuse = await scope.Service.SendAsync("alice", new ChatSendRequest("carol", clientId, "To Carol"));

        Assert.Equal(ChatSendStatus.Invalid, misuse.Status);
        Assert.Equal(1, await scope.Db.ChatMessages.CountAsync());
    }

    [Theory]
    [InlineData("alice", "Talking to myself")]
    [InlineData("bob", "   ")]
    [InlineData("", "No recipient")]
    public async Task Send_InvalidRequests_AreRejected(string recipient, string body)
    {
        await using var scope = await ChatScope.CreateAsync();

        var result = await scope.Service.SendAsync("alice", Request(recipient, body));

        Assert.Equal(ChatSendStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(0, await scope.Db.ChatMessages.CountAsync());
    }

    [Fact]
    public async Task Send_TooLongOrMissingClientId_IsRejected()
    {
        await using var scope = await ChatScope.CreateAsync();

        var tooLong = await scope.Service.SendAsync("alice", Request("bob", new string('x', ChatMessage.MaxBodyLength + 1)));
        var noId = await scope.Service.SendAsync("alice", new ChatSendRequest("bob", Guid.Empty, "Hi"));

        Assert.Equal(ChatSendStatus.Invalid, tooLong.Status);
        Assert.Equal(ChatSendStatus.Invalid, noId.Status);
    }

    [Fact]
    public async Task Send_NormalisesLineEndingsAndTrims()
    {
        await using var scope = await ChatScope.CreateAsync();

        var result = await scope.Service.SendAsync("alice", Request("bob", "  line one\r\nline two \r\n"));

        Assert.Equal("line one\nline two", result.Message!.Body);
    }

    [Theory]
    [InlineData("dave")] // disabled
    [InlineData("erin")] // pending deletion
    [InlineData("svc")] // service account
    [InlineData("nobody")] // unknown id
    public async Task Send_ToUnreachableAccount_IsRefused(string recipient)
    {
        await using var scope = await ChatScope.CreateAsync();

        var result = await scope.Service.SendAsync("alice", Request(recipient, "Are you there?"));

        Assert.Equal(ChatSendStatus.RecipientUnavailable, result.Status);
        Assert.Equal(0, await scope.Db.ChatConversations.CountAsync());
    }

    [Fact]
    public async Task History_IsVisibleOnlyToTheTwoParticipants()
    {
        await using var scope = await ChatScope.CreateAsync();
        await scope.Service.SendAsync("alice", Request("bob", "Private to Bob"));

        var bob = await scope.Service.GetHistoryAsync("bob", "alice", null, 40);
        var carolAskingForAlice = await scope.Service.GetHistoryAsync("carol", "alice", null, 40);
        var carolAskingForBob = await scope.Service.GetHistoryAsync("carol", "bob", null, 40);

        Assert.Single(bob.Messages);
        Assert.Equal("bob", bob.Messages[0].RecipientUserId);
        Assert.Empty(carolAskingForAlice.Messages);
        Assert.Empty(carolAskingForBob.Messages);
    }

    [Fact]
    public async Task History_PagesBackwardsOldestFirst()
    {
        await using var scope = await ChatScope.CreateAsync();
        for (var i = 1; i <= 5; i++)
        {
            await scope.Service.SendAsync("alice", Request("bob", $"m{i}"));
        }

        var latest = await scope.Service.GetHistoryAsync("bob", "alice", null, 2);
        var earlier = await scope.Service.GetHistoryAsync("bob", "alice", latest.Messages[0].Id, 2);
        var earliest = await scope.Service.GetHistoryAsync("bob", "alice", earlier.Messages[0].Id, 2);

        Assert.Equal(new[] { "m4", "m5" }, latest.Messages.Select(m => m.Body));
        Assert.True(latest.HasMore);
        Assert.Equal(new[] { "m2", "m3" }, earlier.Messages.Select(m => m.Body));
        Assert.True(earlier.HasMore);
        Assert.Equal(new[] { "m1" }, earliest.Messages.Select(m => m.Body));
        Assert.False(earliest.HasMore);
    }

    [Fact]
    public async Task MarkRead_ClearsUnread_AndNeverMovesBackwards()
    {
        await using var scope = await ChatScope.CreateAsync();
        var first = await scope.Service.SendAsync("alice", Request("bob", "one"));
        var second = await scope.Service.SendAsync("alice", Request("bob", "two"));

        var marker = await scope.Service.MarkReadAsync("bob", "alice", second.Message!.Id);
        var backwards = await scope.Service.MarkReadAsync("bob", "alice", first.Message!.Id);
        var beyond = await scope.Service.MarkReadAsync("bob", "alice", long.MaxValue);

        Assert.Equal(second.Message.Id, marker);
        Assert.Equal(second.Message.Id, backwards);
        Assert.Equal(second.Message.Id, beyond);
        Assert.Equal(0, (await scope.Service.ListContactsAsync("bob")).Single(c => c.UserId == "alice").UnreadCount);

        await scope.Service.SendAsync("alice", Request("bob", "three"));
        Assert.Equal(1, (await scope.Service.ListContactsAsync("bob")).Single(c => c.UserId == "alice").UnreadCount);
    }

    [Fact]
    public async Task MarkRead_WithoutConversation_ReturnsNull()
    {
        await using var scope = await ChatScope.CreateAsync();

        Assert.Null(await scope.Service.MarkReadAsync("carol", "alice", 1));
    }

    [Fact]
    public async Task Contacts_ListActiveHumans_AndKeepInactivePeersWithHistory()
    {
        await using var scope = await ChatScope.CreateAsync();
        await scope.Service.SendAsync("alice", Request("dave-before", "Before you leave"));
        var dave = await scope.Db.Users.SingleAsync(u => u.Id == "dave-before");
        dave.IsDisabled = true;
        await scope.Db.SaveChangesAsync();
        scope.Presence.Connect("bob", "bob-tab");
        scope.Presence.Connect("carol", "carol-tab");
        scope.Presence.SetAway("carol", "carol-tab", true);

        var contacts = await scope.Service.ListContactsAsync("alice");

        Assert.Equal(new[] { "bob", "carol", "dave-before" }, contacts.Select(c => c.UserId).OrderBy(id => id));
        Assert.False(contacts.Single(c => c.UserId == "dave-before").IsActive);
        Assert.True(contacts.Single(c => c.UserId == "bob").IsOnline);
        Assert.False(contacts.Single(c => c.UserId == "bob").IsAway);
        Assert.True(contacts.Single(c => c.UserId == "carol").IsOnline);
        Assert.True(contacts.Single(c => c.UserId == "carol").IsAway);
        Assert.False(contacts.Single(c => c.UserId == "dave-before").IsOnline);
        Assert.Equal("Maj", contacts.Single(c => c.UserId == "bob").Rank);
        Assert.Equal("BB", contacts.Single(c => c.UserId == "bob").Initials);
        Assert.Equal(ChatSendStatus.RecipientUnavailable,
            (await scope.Service.SendAsync("alice", Request("dave-before", "Still there?"))).Status);
    }

    [Fact]
    public async Task Contacts_KeepAConversationWhosePeerAccountWasPurged()
    {
        await using var scope = await ChatScope.CreateAsync();
        await scope.Service.SendAsync("carol", Request("alice", "Handing over before I leave"));
        scope.Db.Users.Remove(await scope.Db.Users.SingleAsync(u => u.Id == "carol"));
        await scope.Db.SaveChangesAsync();

        var former = (await scope.Service.ListContactsAsync("alice")).Single(c => c.UserId == "carol");
        var history = await scope.Service.GetHistoryAsync("alice", "carol", null, 40);

        Assert.Equal(ChatService.FormerUserName, former.DisplayName);
        Assert.False(former.IsActive);
        Assert.Equal(1, former.UnreadCount);
        Assert.Single(history.Messages);
        Assert.Equal(ChatSendStatus.RecipientUnavailable,
            (await scope.Service.SendAsync("alice", Request("carol", "Still there?"))).Status);
    }

    [Fact]
    public async Task MarkRead_FromAStaleContext_CannotMoveTheMarkerBack()
    {
        await using var scope = await ChatScope.CreateAsync();
        var first = await scope.Service.SendAsync("alice", Request("bob", "one"));
        var second = await scope.Service.SendAsync("alice", Request("bob", "two"));

        // A second request that started earlier and carries the lower marker finishes last.
        await using var otherDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(scope.Connection).Options);
        var other = new ChatService(otherDb, new StepClock(), scope.Presence, new TestOptions());
        await otherDb.ChatParticipants.LoadAsync();

        await scope.Service.MarkReadAsync("bob", "alice", second.Message!.Id);
        var result = await other.MarkReadAsync("bob", "alice", first.Message!.Id);

        Assert.Equal(second.Message.Id, result);
        Assert.Equal(0, (await scope.Service.ListContactsAsync("bob")).Single(c => c.UserId == "alice").UnreadCount);
    }

    [Fact]
    public async Task History_ReportsHowFarThePeerHasRead()
    {
        await using var scope = await ChatScope.CreateAsync();
        var first = await scope.Service.SendAsync("alice", Request("bob", "one"));
        await scope.Service.SendAsync("alice", Request("bob", "two"));

        Assert.Null((await scope.Service.GetHistoryAsync("alice", "bob", null, 40)).PeerLastReadMessageId);

        await scope.Service.MarkReadAsync("bob", "alice", first.Message!.Id);
        Assert.Equal(first.Message.Id, (await scope.Service.GetHistoryAsync("alice", "bob", null, 40)).PeerLastReadMessageId);
    }

    [Fact]
    public async Task History_OpensAtAMessageAndPagesForwardToTheLatest()
    {
        await using var scope = await ChatScope.CreateAsync();
        var ids = new List<long>();
        for (var i = 1; i <= 6; i++)
        {
            ids.Add((await scope.Service.SendAsync("alice", Request("bob", $"m{i}"))).Message!.Id);
        }

        // Open at m3 (as a search hit does): the page ends at m3 and says newer ones exist.
        var atHit = await scope.Service.GetHistoryAsync("bob", "alice", ids[2] + 1, 2);
        Assert.Equal(new[] { "m2", "m3" }, atHit.Messages.Select(m => m.Body));
        Assert.True(atHit.HasMore);
        Assert.True(atHit.HasNewer);

        var newer = await scope.Service.GetHistoryAsync("bob", "alice", null, 2, afterMessageId: ids[2]);
        Assert.Equal(new[] { "m4", "m5" }, newer.Messages.Select(m => m.Body));
        Assert.True(newer.HasNewer);

        var newest = await scope.Service.GetHistoryAsync("bob", "alice", null, 2, afterMessageId: ids[4]);
        Assert.Equal(new[] { "m6" }, newest.Messages.Select(m => m.Body));
        Assert.False(newest.HasNewer);

        Assert.False((await scope.Service.GetHistoryAsync("bob", "alice", null, 40)).HasNewer);
    }

    [Fact]
    public async Task Search_FindsTextOnlyInTheCallersOwnConversations()
    {
        await using var scope = await ChatScope.CreateAsync();
        await scope.Service.SendAsync("alice", Request("bob", "The TEC minutes are signed"));
        await scope.Service.SendAsync("bob", Request("alice", "Thanks, forwarding the tec minutes now"));
        await scope.Service.SendAsync("bob", Request("carol", "TEC minutes for you too"));
        await scope.Service.SendAsync("alice", Request("carol", "Unrelated lunch plan"));

        var alice = await scope.Service.SearchAsync("alice", "  tec MINUTES ", 50);
        var carol = await scope.Service.SearchAsync("carol", "tec", 50);

        Assert.Equal(2, alice.Count);
        Assert.All(alice, hit => Assert.Equal("bob", hit.PeerUserId));
        Assert.Equal("Bob Bhatia", alice[0].PeerDisplayName);
        Assert.True(alice[0].MessageId > alice[1].MessageId); // newest first
        Assert.Single(carol);
        Assert.Equal("bob", carol[0].PeerUserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" a ")]
    public async Task Search_IgnoresTooShortQueries(string? query)
    {
        await using var scope = await ChatScope.CreateAsync();
        await scope.Service.SendAsync("alice", Request("bob", "a message with a"));

        Assert.Empty(await scope.Service.SearchAsync("alice", query, 50));
    }

    [Fact]
    public async Task Search_TreatsWildcardCharactersAsText()
    {
        await using var scope = await ChatScope.CreateAsync();
        await scope.Service.SendAsync("alice", Request("bob", "Progress is 50% done"));
        await scope.Service.SendAsync("alice", Request("bob", "Progress is 500 units"));

        var hits = await scope.Service.SearchAsync("alice", "50%", 50);

        Assert.Single(hits);
        Assert.Equal("Progress is 50% done", hits[0].Body);
    }

    [Fact]
    public async Task Unsend_WithinTheWindow_ErasesTheTextForBothPeople()
    {
        await using var scope = await ChatScope.CreateAsync();
        var sent = await scope.Service.SendAsync("alice", Request("bob", "Oops, wrong person"));

        var result = await scope.Service.UnsendAsync("alice", sent.Message!.Id);

        Assert.Equal(ChatUnsendStatus.Unsent, result.Status);
        Assert.True(result.Message!.IsUnsent);
        Assert.Equal("bob", result.Message.RecipientUserId);
        var stored = await scope.Db.ChatMessages.AsNoTracking().SingleAsync();
        Assert.Equal(string.Empty, stored.Body);
        Assert.NotNull(stored.UnsentUtc);

        var bobView = await scope.Service.GetHistoryAsync("bob", "alice", null, 40);
        Assert.True(bobView.Messages.Single().IsUnsent);
        Assert.Equal(string.Empty, bobView.Messages.Single().Body);
        Assert.Equal(0, (await scope.Service.ListContactsAsync("bob")).Single(c => c.UserId == "alice").UnreadCount);
        Assert.Empty(await scope.Service.SearchAsync("bob", "wrong", 50));

        // Repeating it (a second tab, a retry) is harmless.
        Assert.Equal(ChatUnsendStatus.Unsent, (await scope.Service.UnsendAsync("alice", sent.Message.Id)).Status);
    }

    [Fact]
    public async Task Unsend_AfterTheWindow_IsRefused()
    {
        await using var scope = await ChatScope.CreateAsync();
        var sent = await scope.Service.SendAsync("alice", Request("bob", "Kept forever"));
        scope.Clock.Advance(TimeSpan.FromMinutes(6));

        var result = await scope.Service.UnsendAsync("alice", sent.Message!.Id);

        Assert.Equal(ChatUnsendStatus.TooLate, result.Status);
        Assert.Equal("Kept forever", (await scope.Db.ChatMessages.AsNoTracking().SingleAsync()).Body);
    }

    [Fact]
    public async Task Unsend_SomeoneElsesMessage_IsNotFound()
    {
        await using var scope = await ChatScope.CreateAsync();
        var sent = await scope.Service.SendAsync("alice", Request("bob", "Mine"));

        Assert.Equal(ChatUnsendStatus.NotFound, (await scope.Service.UnsendAsync("bob", sent.Message!.Id)).Status);
        Assert.Equal(ChatUnsendStatus.NotFound, (await scope.Service.UnsendAsync("alice", 9999)).Status);
        Assert.Equal("Mine", (await scope.Db.ChatMessages.AsNoTracking().SingleAsync()).Body);
    }

    [Fact]
    public async Task Status_IsSetNormalisedShownToOthersAndCleared()
    {
        await using var scope = await ChatScope.CreateAsync();

        var stored = await scope.Service.SetStatusAsync("bob", "  In review\nmeeting   till 4  ");
        Assert.Equal("In review meeting till 4", stored);
        Assert.Equal("In review meeting till 4", await scope.Service.GetStatusAsync("bob"));
        Assert.Equal("In review meeting till 4", (await scope.Service.ListContactsAsync("alice")).Single(c => c.UserId == "bob").StatusMessage);

        var updated = await scope.Service.SetStatusAsync("bob", new string('x', 300));
        Assert.Equal(ChatUserStatus.MaxMessageLength, updated!.Length);

        Assert.Null(await scope.Service.SetStatusAsync("bob", "   "));
        Assert.Null(await scope.Service.GetStatusAsync("bob"));
        Assert.Null((await scope.Service.ListContactsAsync("alice")).Single(c => c.UserId == "bob").StatusMessage);
        Assert.Equal(0, await scope.Db.ChatUserStatuses.CountAsync());
    }

    private static ChatSendRequest Request(string recipient, string body)
        => new(recipient, Guid.NewGuid(), body);

    private sealed class ChatScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ChatScope(SqliteConnection connection, ApplicationDbContext db)
        {
            _connection = connection;
            Db = db;
            Presence = new ChatPresenceTracker();
            Clock = new StepClock();
            Service = new ChatService(db, Clock, Presence, new TestOptions());
        }

        public ApplicationDbContext Db { get; }

        public SqliteConnection Connection => _connection;

        public ChatPresenceTracker Presence { get; }

        public StepClock Clock { get; }

        public ChatService Service { get; }

        public static async Task<ChatScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            db.Users.AddRange(
                Human("alice", "Alice Anand"),
                Human("bob", "Bob Bhatia", "Maj"),
                Human("carol", "Carol Chauhan"),
                Human("dave", "Dave Dutta", disabled: true),
                Human("dave-before", "Dave Earlier"),
                new ApplicationUser { Id = "erin", UserName = "erin", FullName = "Erin", AccountKind = UserAccountKind.Human, PendingDeletion = true },
                new ApplicationUser { Id = "svc", UserName = "svc", FullName = "Backup service", AccountKind = UserAccountKind.Service });
            await db.SaveChangesAsync();
            return new ChatScope(connection, db);
        }

        private static ApplicationUser Human(string id, string name, string rank = "", bool disabled = false)
            => new() { Id = id, UserName = id, FullName = name, Rank = rank, AccountKind = UserAccountKind.Human, IsDisabled = disabled };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>Advances one second per read so message times are distinct and ordered.</summary>
    private sealed class StepClock : IClock
    {
        private DateTimeOffset _now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class TestOptions : IOptionsMonitor<ChatOptions>
    {
        public ChatOptions CurrentValue { get; } = new() { UnsendWindowMinutes = 5 };

        public ChatOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ChatOptions, string?> listener) => null;
    }
}
