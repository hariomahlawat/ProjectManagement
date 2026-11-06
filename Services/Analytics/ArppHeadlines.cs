using System.Collections.Generic;
using ProjectManagement.Models.Analytics;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.Analytics;

/// <summary>One ARPP headline card: its label, figure and the line beneath.</summary>
public sealed record ArppHeadline(string Key, string Label, string Value, string Hint);

/// <summary>
/// The five ARPP headline cards, built once from <see cref="ArppAnalyticsVm"/> so the Analytics page and the
/// progress slides always say the same thing. Listed, then where the projects stand: completed, supply
/// order placed, then the rest split by AoN (AoN to SO stage, Pre-AoN stage). Cancelled projects are in no
/// card; the standing board lists them.
/// </summary>
public static class ArppHeadlines
{
    public const string Listed = "listed";
    public const string Completed = "completed";
    public const string SupplyOrder = "supplyOrder";
    public const string AonToSupplyOrder = "aonToSo";
    public const string PreAon = "preAon";

    public static IReadOnlyList<ArppHeadline> Build(ArppAnalyticsVm arpp)
    {
        // A past year counts where the projects stood on its 31 March; the current year, today.
        var when = arpp.AsOfDate is { } asOf ? $"on {asOf:d MMM}" : "ongoing";
        var notKnown = arpp.CardNotKnownCount > 0 ? $" · {arpp.CardNotKnownCount} not known {when}" : string.Empty;
        return new[]
        {
            new ArppHeadline(
                Listed,
                "Listed projects",
                arpp.ListedCount.ToString(),
                $"IPA {IndianCurrencyFormatter.FormatCompact(arpp.ListedIpaValue)} · New {arpp.NewCount} · CL {arpp.CommittedLiabilityCount} · CF {arpp.CarryForwardCount} · delisted {arpp.DelistedCount}{notKnown}"),
            new ArppHeadline(
                Completed,
                "Completed",
                arpp.CompletedCount.ToString(),
                arpp.CompletedSupplyOrderValue > 0
                    ? $"supply orders {IndianCurrencyFormatter.FormatCompact(arpp.CompletedSupplyOrderValue)}"
                    : "no supply-order value recorded"),
            new ArppHeadline(
                SupplyOrder,
                "Supply orders placed",
                arpp.SupplyOrderCount.ToString(),
                arpp.SupplyOrderValue > 0
                    ? $"supply orders {IndianCurrencyFormatter.FormatCompact(arpp.SupplyOrderValue)} · {when}"
                    : $"{when}; no value recorded yet"),
            new ArppHeadline(
                AonToSupplyOrder,
                "AoN to SO stage",
                arpp.AonToSupplyOrderCount.ToString(),
                $"{when}, AoN completed, supply order not yet placed"),
            new ArppHeadline(
                PreAon,
                "Pre-AoN stage",
                arpp.PreAonCount.ToString(),
                $"{when}, AoN not yet completed")
        };
    }
}
