using System;
using System.Security.Claims;
using ProjectManagement.Models.Notifications;
using ProjectManagement.Services.Notifications;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class NotificationGroupingTests
{
    private static readonly DateTime Morning = new(2026, 6, 30, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RoutineUpdatesForTheSameProjectCategoryAndDayShareAKey()
    {
        var published = Key(NotificationKind.DocumentPublished, 42, Morning);
        var replaced = Key(NotificationKind.DocumentReplaced, 42, Morning.AddHours(3));

        Assert.NotNull(published);
        Assert.Equal(published, replaced);
    }

    [Fact]
    public void DifferentProjectCategoryOrIndiaDayGetDifferentKeys()
    {
        var baseline = Key(NotificationKind.DocumentPublished, 42, Morning);

        Assert.NotEqual(baseline, Key(NotificationKind.DocumentPublished, 43, Morning));
        Assert.NotEqual(baseline, Key(NotificationKind.StageStatusChanged, 42, Morning));

        // 18:00 UTC is 23:30 IST; 19:00 UTC is 00:30 IST the next day.
        var lateEvening = Key(NotificationKind.DocumentPublished, 42, new DateTime(2026, 6, 30, 18, 0, 0, DateTimeKind.Utc));
        var afterMidnight = Key(NotificationKind.DocumentPublished, 42, new DateTime(2026, 6, 30, 19, 0, 0, DateTimeKind.Utc));
        Assert.NotEqual(lateEvening, afterMidnight);
    }

    [Fact]
    public void UpdatesWithoutAProjectGroupByCategoryAndDay()
    {
        var first = Key(NotificationKind.ActionTaskProgressUpdated, null, Morning);
        var second = Key(NotificationKind.ActionTaskStatusChanged, null, Morning.AddHours(1));

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.StartsWith("none|", first);
    }

    [Theory]
    [InlineData(NotificationKind.PlanSubmitted)]
    [InlineData(NotificationKind.MentionedInRemark)]
    [InlineData(NotificationKind.ActionTaskBlocked)]
    [InlineData(NotificationKind.DocumentDeleted)]
    [InlineData(NotificationKind.NotebookAccessRemoved)]
    public void ActionRequiredAndHigherPriorityKindsAreNeverGrouped(NotificationKind kind)
        => Assert.Null(Key(kind, 42, Morning));

    [Fact]
    public void SeniorRolesGetOneRemarkGroupPerDayAcrossProjects()
    {
        var orion = RemarkKey(42, Morning, combine: true);
        var vega = RemarkKey(43, Morning.AddHours(2), combine: true);

        Assert.NotNull(orion);
        Assert.Equal(orion, vega);
        Assert.NotEqual(orion, RemarkKey(42, Morning.AddDays(1), combine: true));
        // Other routine kinds keep their per-project groups even for senior roles.
        Assert.NotEqual(
            Key(NotificationKind.DocumentPublished, 42, Morning, combine: true),
            Key(NotificationKind.DocumentPublished, 43, Morning, combine: true));
        // Mentions stay action-required and ungrouped.
        Assert.Null(Key(NotificationKind.MentionedInRemark, 42, Morning, combine: true));
    }

    [Fact]
    public void OtherRecipientsKeepOneRemarkGroupPerProject()
        => Assert.NotEqual(RemarkKey(42, Morning, combine: false), RemarkKey(43, Morning, combine: false));

    [Theory]
    [InlineData("Comdt", true)]
    [InlineData("MCO", true)]
    [InlineData("HoD", true)]
    [InlineData("Project Officer", false)]
    [InlineData("Admin", false)]
    public void CrossProjectRemarkGroupingFollowsTheViewersRole(string role, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Role, role) },
            "Test"));

        Assert.Equal(expected, NotificationGrouping.CombinesRemarksAcrossProjects(principal));
    }

    private static string? RemarkKey(int projectId, DateTime createdUtc, bool combine)
        => Key(NotificationKind.RemarkCreated, projectId, createdUtc, combine);

    private static string? Key(NotificationKind kind, int? projectId, DateTime createdUtc, bool combine)
        => NotificationGrouping.ResolveGroupKey(
            kind,
            NotificationPresentationCatalog.Resolve(kind, module: null, eventType: null),
            projectId,
            createdUtc,
            combine);

    private static string? Key(NotificationKind kind, int? projectId, DateTime createdUtc)
        => NotificationGrouping.ResolveGroupKey(
            NotificationPresentationCatalog.Resolve(kind, module: null, eventType: null),
            projectId,
            createdUtc);
}
