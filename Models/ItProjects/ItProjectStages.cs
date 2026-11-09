using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectManagement.Models.ItProjects;

/// <summary>
/// The fixed, ordered stage list of an IT project. It lives in code, not in a template table,
/// and does not reuse the R&amp;D <c>StageCodes</c> (docs/ux/it-projects.md, "Stages").
/// </summary>
public static class ItProjectStages
{
    public const string Ppp = "PPP";
    public const string Aon = "AON";
    public const string Bid = "BID";
    public const string Tec = "TEC";
    public const string Cob = "COB";
    public const string Benchmarking = "BM";
    public const string Pnc = "PNC";
    public const string So = "SO";
    public const string Delivery = "DLV";

    public sealed record Definition(string Code, int SortOrder, string Label, bool IsOptional, bool CarriesCost);

    /// <summary>Stages in order. Optional stages are skipped by default.</summary>
    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(Ppp, 1, "IT ARPPP / IPA", IsOptional: false, CarriesCost: false),
        new Definition(Aon, 2, "AoN", IsOptional: false, CarriesCost: false),
        new Definition(Bid, 3, "Bid", IsOptional: false, CarriesCost: false),
        new Definition(Tec, 4, "TEC", IsOptional: false, CarriesCost: false),
        new Definition(Cob, 5, "COB", IsOptional: false, CarriesCost: false),
        new Definition(Benchmarking, 6, "Benchmarking", IsOptional: true, CarriesCost: false),
        new Definition(Pnc, 7, "PNC", IsOptional: true, CarriesCost: false),
        new Definition(So, 8, "SO", IsOptional: false, CarriesCost: true),
        new Definition(Delivery, 9, "Delivery and ATP", IsOptional: false, CarriesCost: false)
    };

    private static readonly IReadOnlyDictionary<string, Definition> ByCode =
        All.ToDictionary(definition => definition.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Codes { get; } = All.Select(definition => definition.Code).ToArray();

    public static bool IsKnown(string? code) => code is not null && ByCode.ContainsKey(code);

    public static Definition Get(string code) =>
        ByCode.TryGetValue(code ?? string.Empty, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown IT project stage.");

    public static string LabelOf(string code) => Get(code).Label;

    public static int OrderOf(string code) => Get(code).SortOrder;

    public static bool IsOptional(string code) => Get(code).IsOptional;

    /// <summary>The nine rows a new project starts with: nothing complete, optional stages skipped.</summary>
    public static List<ItProjectStage> NewStageRows() => All
        .Select(definition => new ItProjectStage
        {
            StageCode = definition.Code,
            SortOrder = definition.SortOrder,
            IsCompleted = false,
            CompletedOn = null,
            IsSkipped = definition.IsOptional,
            CostLakhs = null
        })
        .ToList();
}
