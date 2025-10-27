using System;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityIndexPaginationViewModel
{
    public int PageNumber { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public int TotalPages { get; init; }

    public bool HasPrevious => PageNumber > 1;

    public bool HasNext => PageNumber < TotalPages;
}
