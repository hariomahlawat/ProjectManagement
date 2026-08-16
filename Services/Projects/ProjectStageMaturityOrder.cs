using ProjectManagement.Models;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.Projects;

/// <summary>
/// Canonical maturity-first ordering for project lifecycle reporting.
/// Lower values represent a more mature position. A project that is explicitly
/// marked Completed always wins over stage history; callers must not infer a
/// current stage for a completed project.
/// </summary>
public static class ProjectStageMaturityOrder
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

    private static readonly IReadOnlyList<ProjectStageMaturityDefinition> CanonicalStages =
        Array.AsReadOnly(new[]
        {
            new ProjectStageMaturityDefinition(CompletedCode, "Completed", Completed),
            new ProjectStageMaturityDefinition(StageCodes.TOT, "Transfer of Technology", TransferOfTechnology),
            new ProjectStageMaturityDefinition(StageCodes.PAYMENT, "Payment", Payment),
            new ProjectStageMaturityDefinition(StageCodes.ATP, "Acceptance Testing", AcceptanceTesting),
            new ProjectStageMaturityDefinition(StageCodes.DEVP, "Development", Development),
            new ProjectStageMaturityDefinition(StageCodes.SO, "Supply Order", SupplyOrder),
            new ProjectStageMaturityDefinition(StageCodes.EAS, "EAS Approval", EasApproval),
            new ProjectStageMaturityDefinition(StageCodes.PNC, "Price Negotiation", Pnc),
            new ProjectStageMaturityDefinition(StageCodes.COB, "Commercial Opening", CommercialBidOpening),
            new ProjectStageMaturityDefinition(StageCodes.BM, "Benchmarking", Benchmarking),
            new ProjectStageMaturityDefinition(StageCodes.TEC, "Technical Evaluation", TechnicalEvaluation),
            new ProjectStageMaturityDefinition(StageCodes.BID, "Bidding / Tendering", BiddingTendering),
            new ProjectStageMaturityDefinition(StageCodes.AON, "Acceptance of Necessity", AcceptanceOfNecessity),
            new ProjectStageMaturityDefinition(StageCodes.IPA, "In-Principle Approval", InPrincipleApproval),
            new ProjectStageMaturityDefinition(StageCodes.SOW, "Scope of Work Vetting", SowVetting),
            new ProjectStageMaturityDefinition(StageCodes.FS, "Feasibility Study", FeasibilityStudy)
        });

    public static IReadOnlyList<ProjectStageMaturityDefinition> Stages => CanonicalStages;

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

    public static string LabelFor(int order)
        => CanonicalStages.FirstOrDefault(stage => stage.Order == order)?.Label ?? "Stage unresolved";
}

public sealed record ProjectStageMaturityDefinition(string Code, string Label, int Order);
