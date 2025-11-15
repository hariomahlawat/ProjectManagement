using System.Collections.Generic;

namespace ProjectManagement.Areas.Dashboard.Components.ProjectPulse;

// SECTION: Widget view models
public sealed class ProjectPulseVm
{
    public int Total { get; init; }

    public int Completed { get; init; }

    public int Ongoing { get; init; }

    public AllProjectsCard All { get; init; } = new();

    public CompletedCard Done { get; init; } = new();

    public OngoingCard Doing { get; init; } = new();

    public AnalyticsCard Analytics { get; init; } = new();
}

public sealed record AllProjectsCard(
    int Total = 0,
    IReadOnlyList<StatusBucket>? WeeklyBuckets = null,
    string RepositoryUrl = "/Projects");

public sealed record CompletedCard(
    int Count = 0,
    IReadOnlyList<int>? WeeklyCompletions = null,
    string Link = "/Projects?status=Completed");

public sealed record OngoingCard(
    int Count = 0,
    int Overdue = 0,
    IReadOnlyList<int>? WeeklyActive = null,
    string Link = "/Projects?status=Ongoing");

public sealed record AnalyticsCard(
    string CtaUrl = "/Reports/Projects/Analytics");

public sealed record StatusBucket(int Completed, int Ongoing);
