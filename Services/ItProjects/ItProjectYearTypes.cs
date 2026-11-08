using System;
using System.Collections.Generic;
using System.Linq;
using ProjectManagement.Models.ItProjects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Services.ItProjects;

/// <summary>The type of a project in one financial year, and whether it was set by hand.</summary>
public sealed record ItProjectYearTypeInfo(
    int FinancialYearStart,
    ItProjectYearType Type,
    ItProjectYearType AutomaticType,
    bool IsManual)
{
    public string Code => ItProjectYearTypes.Code(Type);

    public string Label => ItProjectYearTypes.Label(Type);
}

/// <summary>
/// The one rule source for New / CL / CF (docs/ux/it-projects-redesign.md, "Year type").
/// A project is New in its own financial year. In each later year it is still live in, it is CL
/// when the SO was placed before that year began (an SO marked complete without a date counts as
/// placed) and CF otherwise. A type set by hand for a year wins over the automatic one.
/// </summary>
public static class ItProjectYearTypes
{
    private static readonly TimeZoneInfo Ist = TimeZoneHelper.GetIst();

    public static string Code(ItProjectYearType type) => type switch
    {
        ItProjectYearType.New => "New",
        ItProjectYearType.CommittedLiability => "CL",
        ItProjectYearType.CarriedForward => "CF",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string Label(ItProjectYearType type) => type switch
    {
        ItProjectYearType.New => "New",
        ItProjectYearType.CommittedLiability => "CL · committed liability",
        ItProjectYearType.CarriedForward => "CF · carried forward",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>The financial year that is running now in India.</summary>
    public static int CurrentFinancialYear(DateTimeOffset utcNow)
        => FinancialYearHelper.GetStartYear(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, Ist).DateTime));

    /// <summary>
    /// Whether the project counts in <paramref name="financialYearStart"/>: always in its own year;
    /// in a later year up to the current one, unless it was delivered or dropped before that year
    /// began. A delivery without a date keeps the project in its own year only.
    /// </summary>
    public static bool IsLive(ItProject project, int financialYearStart, int currentFinancialYearStart)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (financialYearStart == project.FinancialYearStart)
        {
            return true;
        }

        if (financialYearStart < project.FinancialYearStart || financialYearStart > currentFinancialYearStart)
        {
            return false;
        }

        var yearBegins = FinancialYearHelper.GetStartDate(financialYearStart);
        if (project.DroppedOn is { } droppedOn && droppedOn < yearBegins)
        {
            return false;
        }

        if (ItProjectStatus.Resolve(project).Kind == ItProjectStatusKind.Completed)
        {
            var delivered = Stage(project, ItProjectStages.Delivery)?.CompletedOn;
            return delivered is { } on && on >= yearBegins;
        }

        return true;
    }

    /// <summary>The years the project is live in, oldest first.</summary>
    public static IReadOnlyList<int> LiveYears(ItProject project, int currentFinancialYearStart)
    {
        ArgumentNullException.ThrowIfNull(project);
        var last = Math.Max(project.FinancialYearStart, currentFinancialYearStart);
        return Enumerable.Range(project.FinancialYearStart, last - project.FinancialYearStart + 1)
            .Where(year => IsLive(project, year, currentFinancialYearStart))
            .ToList();
    }

    /// <summary>The automatic type, ignoring anything set by hand.</summary>
    public static ItProjectYearType AutomaticType(ItProject project, int financialYearStart)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (financialYearStart <= project.FinancialYearStart)
        {
            return ItProjectYearType.New;
        }

        var so = Stage(project, ItProjectStages.So);
        var placed = so is { IsCompleted: true } &&
            (so.CompletedOn is not { } placedOn || placedOn < FinancialYearHelper.GetStartDate(financialYearStart));
        return placed ? ItProjectYearType.CommittedLiability : ItProjectYearType.CarriedForward;
    }

    /// <summary>The type in that year, or null when the project is not live in it.</summary>
    public static ItProjectYearTypeInfo? Resolve(ItProject project, int financialYearStart, int currentFinancialYearStart)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!IsLive(project, financialYearStart, currentFinancialYearStart))
        {
            return null;
        }

        var automatic = AutomaticType(project, financialYearStart);
        var manual = project.YearTypeOverrides.FirstOrDefault(item => item.FinancialYearStart == financialYearStart);
        return manual is null
            ? new ItProjectYearTypeInfo(financialYearStart, automatic, automatic, IsManual: false)
            : new ItProjectYearTypeInfo(financialYearStart, manual.Type, automatic, IsManual: true);
    }

    /// <summary>The type in every live year, oldest first.</summary>
    public static IReadOnlyList<ItProjectYearTypeInfo> ResolveAll(ItProject project, int currentFinancialYearStart)
        => LiveYears(project, currentFinancialYearStart)
            .Select(year => Resolve(project, year, currentFinancialYearStart)!)
            .ToList();

    private static ItProjectStage? Stage(ItProject project, string code)
        => project.Stages.FirstOrDefault(stage => string.Equals(stage.StageCode, code, StringComparison.OrdinalIgnoreCase));
}
