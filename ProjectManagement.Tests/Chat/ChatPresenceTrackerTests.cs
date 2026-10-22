using ProjectManagement.Services.Chat;
using Xunit;

namespace ProjectManagement.Tests.Chat;

public sealed class ChatPresenceTrackerTests
{
    [Fact]
    public void UserStaysOnlineUntilTheLastTabDisconnects()
    {
        var tracker = new ChatPresenceTracker();

        Assert.True(tracker.Connect("alice"));
        Assert.False(tracker.Connect("alice"));
        Assert.True(tracker.IsOnline("alice"));

        Assert.False(tracker.Disconnect("alice"));
        Assert.True(tracker.IsOnline("alice"));

        Assert.True(tracker.Disconnect("alice"));
        Assert.False(tracker.IsOnline("alice"));
    }

    [Fact]
    public void DisconnectWithoutConnect_IsIgnored()
    {
        var tracker = new ChatPresenceTracker();

        Assert.False(tracker.Disconnect("ghost"));
        Assert.Empty(tracker.OnlineUserIds());
    }
}
