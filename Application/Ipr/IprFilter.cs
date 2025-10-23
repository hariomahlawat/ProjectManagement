using System;
using System.Collections.Generic;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public sealed class IprFilter
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public string? Query { get; set; }

    public IReadOnlyCollection<IprType>? Types { get; init; }

    public IReadOnlyCollection<IprStatus>? Statuses { get; init; }

    public int? ProjectId { get; set; }

    public DateOnly? FiledFrom { get; set; }

    public DateOnly? FiledTo { get; set; }

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
