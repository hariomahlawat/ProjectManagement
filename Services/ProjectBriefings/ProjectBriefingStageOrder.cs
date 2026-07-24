using ProjectManagement.Models;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Defines the authoritative maturity-first sequence used by the project briefing
/// builder and every briefing export. The sequence is the current SDD workflow in
/// reverse progression, with completed projects placed first.
/// </summary>
public static class ProjectBriefingStageOrder
{
    public const string CompletedCode = "COMPLETED";

    public const int Completed = 0;
    public const int TransferOfTechnology = 10;
    public const int Payment = 20;
    public const int AcceptanceTesting = 30;
    public const int Development = 40;
    public const int SupplyOrder = 50;
    public const int EasApproval = 60;
    public const int Pnc = 70;
    public const int CommercialBidOpening = 80;
    public const int Benchmarking = 90;
    public const int TechnicalEvaluation = 100;
    public const int BiddingTendering = 110;
    public const int AcceptanceOfNecessity = 120;
    public const int InPrincipleApproval = 130;
    public const int SowVetting = 140;
    public const int FeasibilityStudy = 150;
    public const int Unknown = 10_000;

    private static readonly IReadOnlyList<ProjectBriefingStageDefinition> CanonicalStages =
        Array.AsReadOnly(new[]
        {
            new ProjectBriefingStageDefinition(CompletedCode, "Completed", Completed),
            new ProjectBriefingStageDefinition(StageCodes.TOT, "Transfer of Technology", TransferOfTechnology),
            new ProjectBriefingStageDefinition(StageCodes.PAYMENT, "Payment", Payment),
            new ProjectBriefingStageDefinition(StageCodes.ATP, "Acceptance Testing", AcceptanceTesting),
            new ProjectBriefingStageDefinition(StageCodes.DEVP, "Development", Development),
            new ProjectBriefingStageDefinition(StageCodes.SO, "Supply Order", SupplyOrder),
            new ProjectBriefingStageDefinition(StageCodes.EAS, "EAS Approval", EasApproval),
            new ProjectBriefingStageDefinition(StageCodes.PNC, "Price Negotiation", Pnc),
            new ProjectBriefingStageDefinition(StageCodes.COB, "Commercial Opening", CommercialBidOpening),
            new ProjectBriefingStageDefinition(StageCodes.BM, "Benchmarking", Benchmarking),
            new ProjectBriefingStageDefinition(StageCodes.TEC, "Technical Evaluation", TechnicalEvaluation),
            new ProjectBriefingStageDefinition(StageCodes.BID, "Bidding / Tendering", BiddingTendering),
            new ProjectBriefingStageDefinition(StageCodes.AON, "Acceptance of Necessity", AcceptanceOfNecessity),
            new ProjectBriefingStageDefinition(StageCodes.IPA, "In-Principle Approval", InPrincipleApproval),
            new ProjectBriefingStageDefinition(StageCodes.SOW, "Scope of Work Vetting", SowVetting),
            new ProjectBriefingStageDefinition(StageCodes.FS, "Feasibility Study", FeasibilityStudy)
        });

    /// <summary>
    /// Complete presentation catalogue, including stages with no selected projects.
    /// </summary>
    public static IReadOnlyList<ProjectBriefingStageDefinition> Stages => CanonicalStages;

    public static int Resolve(ProjectLifecycleStatus lifecycleStatus, string? stageCode)
    {
        if (lifecycleStatus == ProjectLifecycleStatus.Completed
            || string.Equals(stageCode, CompletedCode, StringComparison.OrdinalIgnoreCase))
        {
            return Completed;
        }

        return stageCode?.Trim().ToUpperInvariant() switch
        {
            StageCodes.TOT => TransferOfTechnology,
            StageCodes.PAYMENT => Payment,
            StageCodes.ATP => AcceptanceTesting,
            StageCodes.DEVP => Development,
            StageCodes.SO => SupplyOrder,
            StageCodes.EAS => EasApproval,
            StageCodes.PNC => Pnc,
            StageCodes.COB => CommercialBidOpening,
            StageCodes.BM => Benchmarking,
            StageCodes.TEC => TechnicalEvaluation,
            StageCodes.BID => BiddingTendering,
            StageCodes.AON => AcceptanceOfNecessity,
            StageCodes.IPA => InPrincipleApproval,
            StageCodes.SOW => SowVetting,
            StageCodes.FS => FeasibilityStudy,
            _ => Unknown
        };
    }

    /// <summary>
    /// Builds the complete stage table in canonical order. Zero-count stages remain
    /// visible so that the briefing never suggests that a valid workflow stage is
    /// missing. Unmapped stages are consolidated into one exception row.
    /// </summary>
    public static IReadOnlyList<ProjectBriefingSummaryPoint> BuildCompleteSummary(
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
            .Select(stage => new ProjectBriefingSummaryPoint(
                stage.Label,
                counts.GetValueOrDefault(stage.Order),
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
