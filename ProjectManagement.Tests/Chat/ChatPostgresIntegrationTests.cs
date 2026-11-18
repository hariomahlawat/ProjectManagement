using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Chat;
using ProjectManagement.Services.Chat;
using ProjectManagement.Services;
using Xunit;

namespace ProjectManagement.Tests.Chat;

/// <summary>
/// Replies, edits, reactions and mentions (migration AddChatRepliesReactionsMentions) on
/// PostgreSQL: the migrated schema, its check constraint and cascade, and the queries the SQLite
/// tests cannot vouch for (group locks, set deletes, timestamp comparisons). Creates, migrates
/// and drops its own prism_test_chat_* database on the PRISM_TEST_POSTGRES_CONNECTION server.
/// </summary>
public sealed class ChatPostgresIntegrationTests
{
    [PostgresMigrationFact]
    public async Task RepliesEditsReactionsAndMentions_WorkOnTheMigratedSchema()
    {
        var server = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!);
        var databaseName = $"prism_test_chat_{Guid.NewGuid():N}"[..40];
        var maintenance = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = databaseName }.ConnectionString;

        await ExecuteAsync(maintenance, $"CREATE DATABASE \"{databaseName}\"");
        try
        {
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(target).Options);
            await db.Database.MigrateAsync();
            db.Users.AddRange(Human("alice"), Human("bob"), Human("carol"));
            await db.SaveChangesAsync();

            var clock = new MovableClock(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
            var options = new StaticOptions();
            var chat = new ChatService(db, clock, new ChatPresenceTracker(), options);
            var groups = new ChatGroupService(db, clock, options);

            // A group message mentioning Bob, and Bob's reply to it.
            var groupId = (await groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
            clock.Advance(TimeSpan.FromMinutes(1));
            var ask = await groups.SendAsync("alice", groupId, new ChatGroupSendRequest(Guid.NewGuid(), "@Bob draft please", MentionedUserIds: new[] { "bob", "carol" }));
            Assert.Equal(ChatSendStatus.Sent, ask.Status);
            Assert.Equal(new[] { "bob" }, ask.Message!.MentionedUserIds); // Carol is not in the group
            clock.Advance(TimeSpan.FromMinutes(1));
            var reply = await groups.SendAsync("bob", groupId, new ChatGroupSendRequest(Guid.NewGuid(), "On it", ReplyToMessageId: ask.Message.Id));
            Assert.Equal(ask.Message.Id, reply.Message!.ReplyToMessageId);

            // Carol joins later: she can neither react to nor be mentioned in the earlier message.
            clock.Advance(TimeSpan.FromMinutes(1));
            await groups.AddMembersAsync("alice", groupId, new[] { "carol" });
            Assert.Equal(ChatChangeStatus.NotFound, (await chat.ReactAsync("carol", ask.Message.Id, ChatReactionKind.Like, true)).Status);
            var edit = await chat.EditAsync("alice", ask.Message.Id, new ChatEditRequest("@Bob draft by 1600", new[] { "bob", "carol" }));
            Assert.Equal(ChatChangeStatus.Done, edit.Status);
            Assert.Equal(new[] { "bob" }, edit.Message!.MentionedUserIds);
            Assert.NotNull(edit.Message.EditedUtc);

            // Reactions: on, repeated, off; the check constraint refuses an unknown kind.
            await chat.ReactAsync("bob", ask.Message.Id, ChatReactionKind.Noted, true);
            await chat.ReactAsync("bob", ask.Message.Id, ChatReactionKind.Noted, true);
            var reacted = await chat.ReactAsync("alice", ask.Message.Id, ChatReactionKind.Like, true);
            Assert.Equal(new[] { ChatReactionKind.Noted, ChatReactionKind.Like }, reacted.Reactions!.Reactions.Select(r => r.Kind));
            await chat.ReactAsync("alice", ask.Message.Id, ChatReactionKind.Like, false);
            Assert.Equal(1, await db.ChatMessageReactions.CountAsync());
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(target,
                $"INSERT INTO \"ChatMessageReactions\" (\"MessageId\", \"UserId\", \"Kind\", \"CreatedUtc\") VALUES ({ask.Message.Id}, 'bob', 'heart', now())"));

            var history = await groups.GetHistoryAsync("bob", groupId, null, 40);
            var asked = history!.Messages.Single(m => m.Id == ask.Message.Id);
            Assert.Equal("@Bob draft by 1600", asked.Body);
            Assert.Equal(new[] { "bob" }, asked.Reactions!.Single().UserIds);
            Assert.Equal(ask.Message.Id, history.Messages.Single(m => m.Id == reply.Message.Id).ReplyToMessageId);

            // Unsending clears its mentions and reactions; the reply keeps pointing at it.
            await chat.UnsendAsync("alice", ask.Message.Id);
            Assert.Equal(0, await db.ChatMessageReactions.CountAsync());
            Assert.Equal(0, await db.ChatMessageMentions.CountAsync());

            // Deleting a conversation removes its reactions and mentions with its messages.
            await chat.ReactAsync("alice", reply.Message.Id, ChatReactionKind.Like, true);
            await ExecuteAsync(target, $"DELETE FROM \"ChatConversations\" WHERE \"Id\" = {groupId}");
            Assert.Equal(0, await db.ChatMessageReactions.CountAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(maintenance, $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)");
        }
    }

    private static ApplicationUser Human(string id) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FullName = id,
        AccountKind = UserAccountKind.Human,
    };

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class MovableClock : IClock
    {
        public MovableClock(DateTimeOffset start) => UtcNow = start;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    private sealed class StaticOptions : IOptionsMonitor<ChatOptions>
    {
        public ChatOptions CurrentValue { get; } = new();

        public ChatOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ChatOptions, string?> listener) => null;
    }
}
