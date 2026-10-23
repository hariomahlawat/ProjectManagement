using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Hubs;

namespace ProjectManagement.Tests.Chat;

/// <summary>One push to browsers: which method, to whom ("*" = everyone) and with what.</summary>
public sealed record ChatPush(string Method, IReadOnlyList<string> Targets, object? Payload);

/// <summary>Stands in for SignalR in tests and records every push made through it.</summary>
public sealed class RecordingHubContext : IHubContext<ChatHub, IChatClient>, IHubClients<IChatClient>
{
    public List<ChatPush> Pushes { get; } = new();

    public IHubClients<IChatClient> Clients => this;

    public IGroupManager Groups { get; } = new NoGroups();

    public IChatClient All => Client("*");

    public IChatClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => Client("*");

    public IChatClient Client(string connectionId) => new Recorder(this, new[] { connectionId });

    // Explicit, like Groups below: the name clashes with the IHubContext.Clients property.
    IChatClient IHubClients<IChatClient>.Clients(IReadOnlyList<string> connectionIds) => new Recorder(this, connectionIds.ToList());

    public IChatClient Group(string groupName) => Client("group:" + groupName);

    public IChatClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);

    // Explicit: the name clashes with the IHubContext.Groups property.
    IChatClient IHubClients<IChatClient>.Groups(IReadOnlyList<string> groupNames) => new Recorder(this, groupNames.ToList());

    public IChatClient User(string userId) => new Recorder(this, new[] { userId });

    public IChatClient Users(IReadOnlyList<string> userIds) => new Recorder(this, userIds.ToList());

    private void Record(string method, IReadOnlyList<string> targets, object? payload)
    {
        lock (Pushes)
        {
            Pushes.Add(new ChatPush(method, targets, payload));
        }
    }

    private sealed class Recorder : IChatClient
    {
        private readonly RecordingHubContext _owner;
        private readonly IReadOnlyList<string> _targets;

        public Recorder(RecordingHubContext owner, IReadOnlyList<string> targets)
        {
            _owner = owner;
            _targets = targets;
        }

        private Task Push(string method, object? payload)
        {
            _owner.Record(method, _targets, payload);
            return Task.CompletedTask;
        }

        public Task ReceiveMessage(ChatMessageDto message) => Push(nameof(ReceiveMessage), message);

        public Task ConversationRead(ChatReadDto read) => Push(nameof(ConversationRead), read);

        public Task PeerRead(ChatReadDto read) => Push(nameof(PeerRead), read);

        public Task PresenceChanged(ChatPresenceDto presence) => Push(nameof(PresenceChanged), presence);

        public Task Typing(ChatTypingDto typing) => Push(nameof(Typing), typing);

        public Task MessageUnsent(ChatMessageDto message) => Push(nameof(MessageUnsent), message);

        public Task StatusChanged(ChatStatusDto status) => Push(nameof(StatusChanged), status);

        public Task ReceiveGroupMessage(ChatMessageDto message) => Push(nameof(ReceiveGroupMessage), message);

        public Task GroupMessageUnsent(ChatMessageDto message) => Push(nameof(GroupMessageUnsent), message);

        public Task GroupChanged(ChatGroupDto group) => Push(nameof(GroupChanged), group);

        public Task GroupRemoved(long groupId) => Push(nameof(GroupRemoved), groupId);

        public Task GroupRead(ChatGroupReadDto read) => Push(nameof(GroupRead), read);

        public Task GroupTyping(ChatGroupTypingDto typing) => Push(nameof(GroupTyping), typing);
    }

    private sealed class NoGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
