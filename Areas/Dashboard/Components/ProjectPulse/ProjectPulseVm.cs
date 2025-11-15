using System.Collections.Generic;

namespace ProjectManagement.Areas.Dashboard.Components.ProjectPulse;

// SECTION: View model contracts
public sealed record ProjectPulseVm(
    SummaryBlock Summary,
    CompletedBlock Completed,
    OngoingBlock Ongoing,
    RepositoryBlock Repository,
    AnalyticsBlock Analytics);

public sealed record SummaryBlock(int Total, int Completed, int Ongoing, int Idle);

public sealed record CompletedBlock(
    int TotalCompleted,
    IReadOnlyList<Point> CompletionsByMonth,
    string Link);

public sealed record OngoingBlock(
    int TotalOngoing,
    int OverdueCount,
    IReadOnlyList<Kv> StageDistribution,
    string Link);

public sealed record RepositoryBlock(
    IReadOnlyList<Kv> CategoryBreakdown,
    string Link);

public sealed record AnalyticsBlock(string Link);

public sealed record Point(string Label, int Value);
public sealed record Kv(string Label, int Value);
