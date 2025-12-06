using System;

namespace ProjectManagement.Models.Stages;

public static class StageCodes
{
    public const string FS = "FS";
    public const string IPA = "IPA";
    public const string SOW = "SOW";
    public const string AON = "AON";
    public const string BID = "BID";
    public const string TEC = "TEC";
    public const string BM = "BM";
    public const string COB = "COB";
    public const string PNC = "PNC";
    public const string SO = "SO";
    public const string DEVP = "DEVP";
    public const string ATP = "ATP";
    public const string EAS = "EAS";
    public const string PAYMENT = "PAYMENT";
    public const string TOT = "TOT";

    public static readonly string[] All =
    {    
        FS,
        IPA,
        SOW,
        AON,
        BID,
        TEC,
        BM,
        COB,
        PNC,
        EAS,
        SO,
        DEVP,
        ATP,
        PAYMENT,
        TOT
    };
    private static readonly IWorkflowStageMetadataProvider WorkflowStageMetadataProvider = new WorkflowStageMetadataProvider();

    public static string DisplayNameOf(string code) => DisplayNameOf(null, code);

    public static string DisplayNameOf(string? workflowVersion, string code) =>
        WorkflowStageMetadataProvider.GetDisplayName(workflowVersion, code);

    public static bool IsTot(string? code) => string.Equals(code, TOT, StringComparison.OrdinalIgnoreCase);

    public static bool IsPayment(string? code) => string.Equals(code, PAYMENT, StringComparison.OrdinalIgnoreCase);
}
