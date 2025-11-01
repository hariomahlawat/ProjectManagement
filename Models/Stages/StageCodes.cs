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
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [FS] = "Feasibility Study",
        [IPA] = "In-Principle Approval",
        [SOW] = "SOW Vetting",
        [AON] = "Acceptance of Necessity",
        [BID] = "Bidding/ Tendering",
        [TEC] = "Technical Evaluation",
        [BM] = "Benchmarking",
        [COB] = "Commercial Bid Opening",
        [PNC] = "PNC",
        [EAS] = "EAS Approval",
        [SO] = "Supply Order",
        [DEVP] = "Development",
        [ATP] = "Acceptance Testing/ Trials",
        [PAYMENT] = "Payment",
        [TOT] = "Transfer of Technology"
    };

    public static string DisplayNameOf(string code) =>
        code is null ? "—" : (DisplayNames.TryGetValue(code, out var name) ? name : code);

    public static bool IsTot(string? code) => string.Equals(code, TOT, StringComparison.OrdinalIgnoreCase);

    public static bool IsPayment(string? code) => string.Equals(code, PAYMENT, StringComparison.OrdinalIgnoreCase);
}
