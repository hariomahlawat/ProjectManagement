using ProjectManagement.Models;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Briefing compatibility facade over the application-wide project maturity
/// ordering contract. Keeping this type preserves existing briefing APIs and
/// tests while reports and briefings now share one canonical stage sequence.
/// </summary>
public static class ProjectBriefingStageOrder
{
    public const string CompletedCode = ProjectStageMaturityOrder.CompletedCode;

    public const int Completed = ProjectStageMaturityOrder.Completed;
    public const int TransferOfTechnology = ProjectStageMaturityOrder.TransferOfTechnology;
    public const int Payment = ProjectStageMaturityOrder.Payment;
    public const int AcceptanceTesting = ProjectStageMaturityOrder.AcceptanceTesting;
    public const int Development = ProjectStageMaturityOrder.Development;
    public const int SupplyOrder = ProjectStageMaturityOrder.SupplyOrder;
    public const int EasApproval = ProjectStageMaturityOrder.EasApproval;
    public const int Pnc = ProjectStageMaturityOrder.Pnc;
    public const int CommercialBidOpening = ProjectStageMaturityOrder.CommercialBidOpening;
    public const int Benchmarking = ProjectStageMaturityOrder.Benchmarking;
    public const int TechnicalEvaluation = ProjectStageMaturityOrder.TechnicalEvaluation;
    public const int BiddingTendering = ProjectStageMaturityOrder.BiddingTendering;
    public const int AcceptanceOfNecessity = ProjectStageMaturityOrder.AcceptanceOfNecessity;
    public const int InPrincipleApproval = ProjectStageMaturityOrder.InPrincipleApproval;
    public const int SowVetting = ProjectStageMaturityOrder.SowVetting;
    public const int FeasibilityStudy = ProjectStageMaturityOrder.FeasibilityStudy;
    public const int Unknown = ProjectStageMaturityOrder.Unknown;

    private static readonly IReadOnlyList<ProjectBriefingStageDefinition> CanonicalStages =
        Array.AsReadOnly(ProjectStageMaturityOrder.Stages
            .Select(stage => new ProjectBriefingStageDefinition(stage.Code, stage.Label, stage.Order))
            .ToArray());

    public static IReadOnlyList<ProjectBriefingStageDefinition> Stages => CanonicalStages;

    public static int Resolve(ProjectLifecycleStatus lifecycleStatus, string? stageCode)
        => ProjectStageMaturityOrder.Resolve(lifecycleStatus, stageCode);

    public static IReadOnlyList<ProjectBriefingSummaryPoint> BuildSummary(
        IEnumerable<int> stageOrders)
    {
        ArgumentNullException.ThrowIfNull(stageOrders);

        var counts = stageOrders
            .GroupBy(order => order)
            .ToDictionary(group => group.Key, group => group.Count());
        var knownOrders = CanonicalStages
            .Select(stage => stage.Order)
            .ToHashSet();

        var result = CanonicalStages
            .Where(stage => counts.GetValueOrDefault(stage.Order) > 0)
            .Select(stage => new ProjectBriefingSummaryPoint(
                stage.Label,
                counts[stage.Order],
                stage.Order))
            .ToList();

        var unresolvedCount = counts
            .Where(pair => !knownOrders.Contains(pair.Key))
            .Sum(pair => pair.Value);
        if (unresolvedCount > 0)
        {
            result.Add(new ProjectBriefingSummaryPoint("Stage unresolved", unresolvedCount, Unknown));
        }

        return result;
    }
}

public sealed record ProjectBriefingStageDefinition(string Code, string Label, int Order);
