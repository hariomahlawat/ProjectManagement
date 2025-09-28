using System;
using System.Collections.Generic;

namespace ProjectManagement.Models.Stages;

public static class StageCodes
{
    public const string EOI = "EOI";
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
        PAYMENT
    };
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [EOI] = "Expression of Interest",
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
        [PAYMENT] = "Payment"
    };

    public static string DisplayNameOf(string code) =>
        code is null ? "—" : (DisplayNames.TryGetValue(code, out var name) ? name : code);
}
