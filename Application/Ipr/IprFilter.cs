using System;
using System.Collections.Generic;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public enum IprDateBasis
{
    Filed = 0,
    Protected = 1
}

public enum IprLinkageFilter
{
    All = 0,
    Linked = 1,
    Unassigned = 2
}

public enum IprEvidenceFilter
{
    All = 0,
    Available = 1,
    Missing = 2
}

/// <summary>How long a pending filing has waited since filing (IST calendar days).</summary>
public enum IprPendingAge
{
    Any = 0,
    UnderOneYear = 1,
    OneToThreeYears = 2,
    ThreeToFiveYears = 3,
    OverFiveYears = 4
}

public sealed class IprFilter
{
    private const int DefaultPageSize = 15;
    private const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public string? Query { get; set; }

    public IReadOnlyCollection<IprType>? Types { get; init; }

    public IReadOnlyCollection<IprStatus>? Statuses { get; init; }

    public int? ProjectId { get; set; }

    public IprDateBasis DateBasis { get; set; } = IprDateBasis.Filed;

    public int? Year { get; set; }

    public IprLinkageFilter Linkage { get; set; } = IprLinkageFilter.All;

    public IprEvidenceFilter Evidence { get; set; } = IprEvidenceFilter.All;

    // Limits the result to pending filings of that age; AsOf (default: today in IST) anchors it.
    public IprPendingAge PendingAge { get; set; } = IprPendingAge.Any;

    public DateOnly? AsOf { get; set; }

    // Retained for compatibility with existing callers and tests that use explicit ranges.
    public DateOnly? FiledFrom { get; set; }

    public DateOnly? FiledTo { get; set; }

    public DateOnly? ProtectedFrom { get; set; }

    public DateOnly? ProtectedTo { get; set; }

    public int Page
    {
        get => _page;
        set => _page = value <= 0 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value <= 0)
            {
                _pageSize = DefaultPageSize;
                return;
            }

            _pageSize = value > MaxPageSize ? MaxPageSize : value;
        }
    }
}
