using ProjectManagement.Models;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Services.ProjectBriefings;

/// <summary>
/// Defines the single presentation sequence used by the deck builder, executive
/// tables, stage summaries and detailed project slides.
/// </summary>
public static class ProjectBriefingStageOrder
{
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
    public const int SowVetting = 130;
    public const int InPrincipleApproval = 140;
    public const int FeasibilityStudy = 150;
    public const int Unknown = 10_000;

    public static int Resolve(ProjectLifecycleStatus lifecycleStatus, string? stageCode)
    {
        if (lifecycleStatus == ProjectLifecycleStatus.Completed
            || string.Equals(stageCode, "COMPLETED", StringComparison.OrdinalIgnoreCase))
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
            StageCodes.SOW => SowVetting,
            StageCodes.IPA => InPrincipleApproval,
            StageCodes.FS => FeasibilityStudy,
            _ => Unknown
        };
    }
}
