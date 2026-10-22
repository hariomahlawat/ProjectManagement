using ProjectManagement.Services.Chat;
using Xunit;

namespace ProjectManagement.Tests.Chat;

public sealed class ChatPresenceTrackerTests
{
    [Fact]
    public void UserStaysOnlineUntilTheLastTabDisconnects()
    {
        var tracker = new ChatPresenceTracker();

        Assert.Equal((ChatPresence.Offline, ChatPresence.Online), tracker.Connect("alice", "tab-1"));
        Assert.Equal((ChatPresence.Online, ChatPresence.Online), tracker.Connect("alice", "tab-2"));

        Assert.Equal((ChatPresence.Online, ChatPresence.Online), tracker.Disconnect("alice", "tab-1"));
        Assert.True(tracker.IsOnline("alice"));

        Assert.Equal((ChatPresence.Online, ChatPresence.Offline), tracker.Disconnect("alice", "tab-2"));
        Assert.False(tracker.IsOnline("alice"));
    }

    [Fact]
    public void UserIsAwayOnlyWhenEveryTabIsIdle()
    {
        var tracker = new ChatPresenceTracker();
        tracker.Connect("alice", "tab-1");
        tracker.Connect("alice", "tab-2");

        Assert.Equal((ChatPresence.Online, ChatPresence.Online), tracker.SetAway("alice", "tab-1", true));
        Assert.Equal((ChatPresence.Online, ChatPresence.Away), tracker.SetAway("alice", "tab-2", true));
        Assert.Equal(ChatPresence.Away, tracker.StatusOf("alice"));
        Assert.True(tracker.IsOnline("alice"));

        // Opening a fresh tab, or activity in either, brings the user back.
        Assert.Equal((ChatPresence.Away, ChatPresence.Online), tracker.Connect("alice", "tab-3"));
        Assert.Equal((ChatPresence.Online, ChatPresence.Away), tracker.Disconnect("alice", "tab-3"));
        Assert.Equal((ChatPresence.Away, ChatPresence.Online), tracker.SetAway("alice", "tab-1", false));
    }

    [Fact]
    public void ClosingTheOnlyActiveTabLeavesTheUserAway()
    {
        var tracker = new ChatPresenceTracker();
        tracker.Connect("alice", "active");
        tracker.Connect("alice", "idle");
        tracker.SetAway("alice", "idle", true);

        Assert.Equal((ChatPresence.Online, ChatPresence.Away), tracker.Disconnect("alice", "active"));
    }

    [Fact]
    public void LateOrUnknownCallsAreIgnored()
    {
        var tracker = new ChatPresenceTracker();

        Assert.Equal((ChatPresence.Offline, ChatPresence.Offline), tracker.Disconnect("ghost", "tab"));
        Assert.Equal((ChatPresence.Offline, ChatPresence.Offline), tracker.SetAway("ghost", "tab", false));
        Assert.Empty(tracker.Snapshot());

        tracker.Connect("alice", "tab-1");
        tracker.Disconnect("alice", "tab-1");
        tracker.SetAway("alice", "tab-1", false); // arrives after the tab closed
        Assert.Equal(ChatPresence.Offline, tracker.StatusOf("alice"));
    }
}
