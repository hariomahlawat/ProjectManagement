using System;
using System.Collections.Generic;

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

    // The short labels every stage chart draws on its axis (wwwroot/js/analytics-projects.js keeps the
    // same table for the charts); generated slides use them as stage tags.
    private static readonly IReadOnlyDictionary<string, string> ShortLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [FS] = "FS", [IPA] = "IPA", [SOW] = "SoW", [AON] = "AoN", [BID] = "Bid", [TEC] = "TEC", [BM] = "BM", [COB] = "COB",
        [PNC] = "PNC", [EAS] = "EAS", [SO] = "SO", [DEVP] = "Devp", [ATP] = "ATP", [PAYMENT] = "Pay", [TOT] = "ToT",
        // Not stages: the buckets the ARPP chart adds for a project with no stage recorded, or not known.
        ["UNASSIGNED"] = "Unassigned", ["NOT_KNOWN"] = "Not known"
    };

    /// <summary>The stage's short label ("Devp", "AoN"); an unknown code is returned as it is.</summary>
    public static string ShortLabelOf(string code) => ShortLabels.TryGetValue(code, out var label) ? label : code;
}
